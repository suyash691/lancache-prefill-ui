using System.Security.Cryptography;
using SteamKit2;
using SteamKit2.CDN;

namespace LancachePrefill;

public class DepotDownloader : IDepotDownloader
{
    private readonly ISteamSession _session;
    private readonly ILogger<DepotDownloader> _log;
    private readonly HttpClient _http;
    private readonly string? _lancacheCacheDir;
    private string? _lancacheIp;
    private Server? _cdnServer;

    public DepotDownloader(ISteamSession session, ILogger<DepotDownloader> log)
    {
        _session = session;
        _log = log;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "Valve/Steam HTTP Client 1.0");
        _lancacheCacheDir = Environment.GetEnvironmentVariable("LANCACHE_CACHE_DIR");
    }

    public async Task<string?> DetectLancacheAsync()
    {
        if (_lancacheIp != null) return _lancacheIp;
        try
        {
            foreach (var addr in await System.Net.Dns.GetHostAddressesAsync("lancache.steamcontent.com"))
                if (NetworkUtils.IsPrivateIp(addr))
                {
                    _lancacheIp = addr.ToString();
                    _log.LogInformation("Lancache at {Ip}", _lancacheIp);
                    return _lancacheIp;
                }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Failed to resolve lancache.steamcontent.com"); }
        return null;
    }

    public async Task<Server> GetCdnServerAsync()
    {
        if (_cdnServer != null) return _cdnServer;
        var servers = await _session.SteamContent.GetServersForSteamPipe();
        _cdnServer = servers
            .Where(s => (s.Type == "SteamCache" || s.Type == "CDN") && s.AllowedAppIds.Length == 0)
            .OrderBy(s => s.Load)
            .FirstOrDefault() ?? throw new InvalidOperationException("No CDN servers available");
        _log.LogInformation("CDN: {Host}", _cdnServer.Host);
        return _cdnServer;
    }

    public async Task<List<DownloadChunk>> GetManifestChunksAsync(DepotState depot)
    {
        var manifestCode = await GetManifestCodeWithTimeout(depot.DepotId, depot.ContainingAppId, depot.ManifestId);
        if (manifestCode == 0 && depot.ContainingAppId != depot.AppId)
            manifestCode = await GetManifestCodeWithTimeout(depot.DepotId, depot.AppId, depot.ManifestId);
        if (manifestCode == 0)
            throw new InvalidOperationException(string.Format("No manifest code for depot {0}", depot.DepotId));

        var server = await GetCdnServerAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var manifest = await Task.Run(async () =>
            await _session.CdnClient.DownloadManifestAsync(depot.DepotId, depot.ManifestId, manifestCode, server), cts.Token)
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
        List<DownloadChunk> chunks, int concurrency = 30,
        IProgress<(long bytes, int done, int total)>? progress = null,
        CancellationToken ct = default)
    {
        var lancacheIp = await DetectLancacheAsync()
            ?? throw new InvalidOperationException("No Lancache detected");
        var cdnServer = await GetCdnServerAsync();

        var errors = new List<string>();
        int ok = 0, failed = 0;
        long totalBytes = 0;

        // Pass 1: Download all chunks
        var failedChunks = new System.Collections.Concurrent.ConcurrentBag<(DownloadChunk chunk, string error)>();

        await Parallel.ForEachAsync(chunks,
            new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = ct },
            async (chunk, token) =>
            {
                var error = await TryDownloadChunk(chunk, lancacheIp, cdnServer, token);
                if (error == null)
                {
                    Interlocked.Increment(ref ok);
                    Interlocked.Add(ref totalBytes, chunk.CompressedLength);
                }
                else
                    failedChunks.Add((chunk, error));

                progress?.Report((Interlocked.Read(ref totalBytes),
                    Interlocked.CompareExchange(ref ok, 0, 0) + failedChunks.Count,
                    chunks.Count));
            });

        // Pass 2: Retry failed chunks (one at a time with longer timeout)
        if (failedChunks.Count > 0 && !ct.IsCancellationRequested)
        {
            _log.LogInformation("Retrying {Count} failed chunks", failedChunks.Count);
            var stillFailing = new List<(DownloadChunk chunk, string error)>();

            foreach (var (chunk, firstError) in failedChunks)
            {
                if (ct.IsCancellationRequested) break;
                await Task.Delay(500, ct); // Brief pause before retry
                var retryError = await TryDownloadChunk(chunk, lancacheIp, cdnServer, ct, timeoutSec: 30);
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

        return new ChunkDownloadResult(ok, failed, totalBytes, errors);
    }

    private async Task<string?> TryDownloadChunk(DownloadChunk chunk, string lancacheIp, Server cdnServer, CancellationToken ct, int timeoutSec = 15)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"http://{lancacheIp}/depot/{chunk.DepotId}/chunk/{chunk.ChunkId}");
            req.Headers.Host = cdnServer.Host;

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            resp.EnsureSuccessStatusCode();

            var buf = new byte[8192];
            using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            while (await stream.ReadAsync(buf, cts.Token) > 0) { }

            return null; // Success
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return "timeout"; }
        catch (HttpRequestException ex) { return $"HTTP {ex.StatusCode}"; }
        catch (Exception ex) { return ex.Message; }
    }

    // Legacy interface method — delegates to new retry method
    public async Task<(int ok, int failed, long bytes)> DownloadChunksAsync(
        List<DownloadChunk> chunks, int concurrency = 30,
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