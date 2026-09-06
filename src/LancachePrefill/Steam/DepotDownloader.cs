using System.Security.Cryptography;
using SteamKit2;
using SteamKit2.CDN;

namespace LancachePrefill;

public class DepotDownloader : IDepotDownloader
{
    private readonly ISteamSession _session;
    private readonly ILogger<DepotDownloader> _log;
    private readonly HttpClient _http;
    private readonly BandwidthLimiter _limiter = new();
    private readonly string? _lancacheCacheDir;
    private string? _lancacheIp;
    private DateTime _lancacheCheckedAt;
    private static readonly TimeSpan DetectSuccessTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DetectFailureTtl = TimeSpan.FromSeconds(30);
    private List<SteamKit2.CDN.Server> _cdnServers = new();
    private int _cdnRotation;
    private DateTime _cdnServerFetchedAt;
    /// <summary>Number of CDN hosts to spread chunk fetches across.</summary>
    private const int CdnPoolSize = 8;

    /// <summary>Monolithic lancache stores content in 1 MiB slices (nginx `slice 1m`).</summary>
    private const int SliceSize = 1_048_576;

    public DepotDownloader(ISteamSession session, ILogger<DepotDownloader> log)
    {
        _session = session;
        _log = log;
        _http = new HttpClient();
        // Timeouts are controlled per-request with idle detection (see TryDownloadChunk).
        // The default 100s HttpClient timeout would silently override them.
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.Add("User-Agent", "Valve/Steam HTTP Client 1.0");
        _lancacheCacheDir = Environment.GetEnvironmentVariable("LANCACHE_CACHE_DIR");
    }

    public async Task<string?> DetectLancacheAsync()
    {
        // TTL cache both ways: successes re-verify every 5 min (picks up an IP
        // change), failures are cached only 30s — so fixing DNS after seeing
        // "not detected" is noticed quickly, while the unauthenticated
        // /api/lancache endpoint can't drive a DNS resolve per request.
        var age = DateTime.UtcNow - _lancacheCheckedAt;
        if (_lancacheCheckedAt != default && age < (_lancacheIp != null ? DetectSuccessTtl : DetectFailureTtl))
            return _lancacheIp;
        try
        {
            string? found = null;
            foreach (var addr in await System.Net.Dns.GetHostAddressesAsync("lancache.steamcontent.com"))
                if (NetworkUtils.IsPrivateIp(addr))
                {
                    found = addr.ToString();
                    break;
                }
            if (found != null && found != _lancacheIp)
                _log.LogInformation("Lancache at {Ip}", found);
            // Resolved successfully: a result with no private IP genuinely means
            // "no lancache" — clear any previous value.
            _lancacheIp = found;
        }
        catch (Exception ex)
        {
            // DNS error: keep the last-known-good IP (stale-if-error) so a
            // transient resolver blip doesn't fail an otherwise healthy run.
            _log.LogWarning(ex, "Failed to resolve lancache.steamcontent.com{Kept}",
                _lancacheIp != null ? " — keeping last-known IP" : "");
        }
        _lancacheCheckedAt = DateTime.UtcNow;
        return _lancacheIp;
    }

    public async Task<Server> GetCdnServerAsync() => (await GetCdnServersAsync())[0];

    /// <summary>
    /// Top CDN servers by load, refreshed every 30 min. Chunk downloads rotate
    /// across the pool — a single pinned upstream is otherwise the throughput
    /// ceiling on first-fill, and one bad host degrades the whole run.
    /// </summary>
    public async Task<List<Server>> GetCdnServersAsync()
    {
        if (_cdnServers.Count > 0 && DateTime.UtcNow - _cdnServerFetchedAt < TimeSpan.FromMinutes(30))
            return _cdnServers;
        try
        {
            var servers = await _session.SteamContent.GetServersForSteamPipe();
            var pool = servers
                .Where(s => (s.Type == "SteamCache" || s.Type == "CDN") && s.AllowedAppIds.Length == 0)
                .OrderBy(s => s.Load)
                .Take(CdnPoolSize)
                .ToList();
            if (pool.Count == 0) throw new InvalidOperationException("No CDN servers available");
            _cdnServers = pool;
            _cdnServerFetchedAt = DateTime.UtcNow;
            _log.LogInformation("CDN pool: {Hosts}", string.Join(", ", pool.Select(s => s.Host)));
            return _cdnServers;
        }
        catch when (_cdnServers.Count > 0)
        {
            // Refresh failed but we still have working servers — keep using them.
            return _cdnServers;
        }
    }

    private Server NextCdnServer(List<Server> pool) =>
        pool[(int)((uint)Interlocked.Increment(ref _cdnRotation) % pool.Count)];

    public async Task<List<DownloadChunk>> GetManifestChunksAsync(DepotState depot)
    {
        var manifestCode = await GetManifestCodeWithTimeout(depot.DepotId, depot.ContainingAppId, depot.ManifestId);
        if (manifestCode == 0 && depot.ContainingAppId != depot.AppId)
            manifestCode = await GetManifestCodeWithTimeout(depot.DepotId, depot.AppId, depot.ManifestId);
        if (manifestCode == 0)
            throw new InvalidOperationException(string.Format("No manifest code for depot {0}", depot.DepotId));

        var server = await GetCdnServerAsync();
        var manifest = await Task.Run(() =>
            _session.CdnClient.DownloadManifestAsync(depot.DepotId, depot.ManifestId, manifestCode, server))
            .WaitAsync(TimeSpan.FromSeconds(30))
            ?? throw new InvalidOperationException(string.Format("Manifest download failed for depot {0}", depot.DepotId));

        return (manifest.Files ?? [])
            .SelectMany(f => f.Chunks)
            .DistinctBy(c => Convert.ToHexString(c.ChunkID!))
            .Select(c => new DownloadChunk(depot.DepotId, Convert.ToHexString(c.ChunkID!).ToLowerInvariant(), c.CompressedLength))
            .ToList();
    }

    private async Task<ulong> GetManifestCodeWithTimeout(uint depotId, uint appId, ulong manifestId)
    {
        try
        {
            var task = _session.SteamContent.GetManifestRequestCode(depotId, appId, manifestId, "public");
            var winner = await Task.WhenAny(task, Task.Delay(10_000));
            return winner == task ? await task : 0;
        }
        catch { return 0; }
    }

    public async Task<ChunkDownloadResult> DownloadChunksWithRetryAsync(
        List<DownloadChunk> chunks, int concurrency = 6,
        IProgress<(long bytes, int done, int total)>? progress = null,
        CancellationToken ct = default, long maxBytesPerSec = 0, bool verifyCached = false)
    {
        var lancacheIp = await DetectLancacheAsync()
            ?? throw new InvalidOperationException("No Lancache detected");
        var cdnPool = await GetCdnServersAsync();

        concurrency = Math.Clamp(concurrency, 1, 30);
        _limiter.SetRate(maxBytesPerSec);

        var errors = new List<string>();
        int ok = 0, failed = 0, skippedCached = 0;
        long totalBytes = 0, cachedBytes = 0;

        // Pass 1: Download all chunks
        var failedChunks = new System.Collections.Concurrent.ConcurrentBag<(DownloadChunk chunk, string error)>();

        await Parallel.ForEachAsync(chunks,
            new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = ct },
            async (chunk, token) =>
            {
                // Already on disk? Skip the fetch entirely. Re-pulling cache HITs just
                // hammers the lancache disk while it is serving real clients.
                // (Only safe for single-slice chunks — the probe checks slice 0.)
                // verifyCached (force prefill) disables the skip so every chunk is re-pulled
                // through the cache and size-validated — repairing poisoned entries.
                if (!verifyCached && chunk.CompressedLength <= SliceSize && await ProbeChunkCachedAsync(chunk) == true)
                {
                    Interlocked.Increment(ref ok);
                    Interlocked.Increment(ref skippedCached);
                    Interlocked.Add(ref cachedBytes, chunk.CompressedLength);
                }
                else
                {
                    var error = await TryDownloadChunk(chunk, lancacheIp, NextCdnServer(cdnPool), token);
                    if (error == null)
                    {
                        Interlocked.Increment(ref ok);
                        Interlocked.Add(ref totalBytes, chunk.CompressedLength);
                    }
                    else
                        failedChunks.Add((chunk, error));
                }

                progress?.Report((Interlocked.Read(ref totalBytes),
                    Interlocked.CompareExchange(ref ok, 0, 0) + failedChunks.Count,
                    chunks.Count));
            });

        if (skippedCached > 0)
            _log.LogInformation("Skipped {Count} already-cached chunks", skippedCached);

        // Pass 2: Retry failed chunks (one at a time, more patient idle timeout).
        // Retries bypass the cache entry (?nocache=1): if the failure was a size mismatch,
        // the cache holds junk under this key and the bypass re-fetch overwrites it in place.
        if (failedChunks.Count > 0 && !ct.IsCancellationRequested)
        {
            _log.LogInformation("Retrying {Count} failed chunks (cache-busting)", failedChunks.Count);
            cdnPool = await GetCdnServersAsync(); // may rotate to healthier servers (TTL)
            var stillFailing = new List<(DownloadChunk chunk, string error)>();

            foreach (var (chunk, firstError) in failedChunks)
            {
                if (ct.IsCancellationRequested) break;
                await Task.Delay(500, ct); // Brief pause before retry
                var retryError = await TryDownloadChunk(chunk, lancacheIp, NextCdnServer(cdnPool), ct, idleTimeoutSec: 120, bustCache: true);
                if (retryError == null)
                {
                    Interlocked.Increment(ref ok);
                    Interlocked.Add(ref totalBytes, chunk.CompressedLength);
                }
                else
                    stillFailing.Add((chunk, retryError));
            }

            failed = stillFailing.Count;
            foreach (var (chunk, error) in stillFailing)
            {
                var msg = $"Chunk depot/{chunk.DepotId}/{chunk.ChunkId[..8]}...: {error}";
                errors.Add(msg);
                _log.LogWarning("Chunk failed after retry: depot {DepotId} chunk {ChunkId}: {Error}", chunk.DepotId, chunk.ChunkId[..8], error);
            }
        }
        else
            failed = failedChunks.Count;

        return new ChunkDownloadResult(ok, failed, totalBytes, errors, cachedBytes);
    }

    private async Task<string?> TryDownloadChunk(DownloadChunk chunk, string lancacheIp, Server cdnServer, CancellationToken ct, int idleTimeoutSec = 60, bool bustCache = false)
    {
        try
        {
            // The overall cap is deliberately generous. Lancache (monolithic) runs nginx with
            // proxy_ignore_client_abort ON and proxy_cache_lock ON: aborting a request does NOT
            // stop the cache's upstream fetch — it keeps downloading in the background while
            // holding the cache lock for that slice. An aggressive abort-and-retry loop therefore
            // multiplies upstream work and starves real LAN clients. Waiting (bounded by an idle
            // check) is strictly cheaper than aborting.
            using var overall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            overall.CancelAfter(TimeSpan.FromMinutes(10));

            // IPv6 literals must be bracketed in a URL authority.
            var hostForUrl = lancacheIp.Contains(':') ? $"[{lancacheIp}]" : lancacheIp;
            // ?nocache=1 hits monolithic's `proxy_cache_bypass $arg_nocache`: the response is
            // re-fetched from upstream and RE-STORED under the same key (the cache key ignores
            // query args) — this repairs a poisoned/corrupt cache entry in place.
            var url = $"http://{hostForUrl}/depot/{chunk.DepotId}/chunk/{chunk.ChunkId}"
                + (bustCache ? "?nocache=1" : "");
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Host = cdnServer.Host;

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, overall.Token);
            resp.EnsureSuccessStatusCode();

            var buf = new byte[8192];
            long received = 0;
            using var stream = await resp.Content.ReadAsStreamAsync(overall.Token);
            while (true)
            {
                int n;
                using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(overall.Token))
                {
                    readCts.CancelAfter(TimeSpan.FromSeconds(idleTimeoutSec));
                    try { n = await stream.ReadAsync(buf, readCts.Token); }
                    catch (OperationCanceledException) when (!overall.Token.IsCancellationRequested)
                    {
                        return $"stalled (no data for {idleTimeoutSec}s)";
                    }
                }
                if (n == 0) break;
                received += n;
                await _limiter.WaitAsync(n, overall.Token);
            }

            // Integrity check: the manifest tells us the exact on-CDN size of every chunk.
            // A mismatch means the cache holds junk for this key (e.g. an upstream error page
            // cached with a 200) — real Steam clients would fail SHA validation on it and
            // retry-loop at 0 B/s. Returning an error routes this chunk into the retry pass,
            // which re-fetches with ?nocache=1 and overwrites the bad entry.
            if (chunk.CompressedLength > 0 && received != chunk.CompressedLength)
                return $"size mismatch (got {received}, expected {chunk.CompressedLength})";

            return null; // Success
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return "timeout"; }
        catch (HttpRequestException ex) { return $"HTTP {ex.StatusCode}"; }
        catch (Exception ex) { return ex.Message; }
    }

    // Legacy interface method — delegates to new retry method
    public async Task<(int ok, int failed, long bytes)> DownloadChunksAsync(
        List<DownloadChunk> chunks, int concurrency = 6,
        IProgress<(long bytes, int done, int total)>? progress = null,
        CancellationToken ct = default)
    {
        var result = await DownloadChunksWithRetryAsync(chunks, concurrency, progress, ct);
        return (result.Ok, result.Failed, result.Bytes);
    }

    public Task<bool?> ProbeChunkCachedAsync(DownloadChunk chunk)
    {
        if (string.IsNullOrEmpty(_lancacheCacheDir)) return Task.FromResult<bool?>(null);

        var key = $"steam/depot/{chunk.DepotId}/chunk/{chunk.ChunkId.ToLowerInvariant()}bytes=0-1048575";
        var hash = MD5.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();

        var path = Path.Combine(_lancacheCacheDir, hex[^2..], hex[^4..^2], hex);
        return Task.FromResult<bool?>(File.Exists(path));
    }
}