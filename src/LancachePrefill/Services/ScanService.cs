using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LancachePrefill.Data.Repositories;
using Microsoft.Extensions.Localization;

namespace LancachePrefill.Services;

public class ScanService
{
    private readonly IAppInfoProvider _appInfo;
    private readonly IDepotDownloader _downloader;
    private readonly IAppRepository _appRepo;
    private readonly ICacheRepository _cacheRepo;
    private readonly IScanRepository _scanRepo;
    private readonly JobCoordinator _jobs;
    private readonly ILogger<ScanService> _log;
    private readonly IStringLocalizer<Messages> _L;
    private CacheBrowserService? _cacheBrowser;

    public ScanService(IAppInfoProvider appInfo, IDepotDownloader downloader,
        IAppRepository appRepo, ICacheRepository cacheRepo, IScanRepository scanRepo,
        JobCoordinator jobs, ILogger<ScanService> log, IStringLocalizer<Messages> L)
    {
        _appInfo = appInfo;
        _downloader = downloader;
        _appRepo = appRepo;
        _cacheRepo = cacheRepo;
        _scanRepo = scanRepo;
        _jobs = jobs;
        _log = log;
        _L = L;
    }

    public void SetCacheBrowser(CacheBrowserService cb) => _cacheBrowser = cb;

    public void RestoreFromDb()
    {
        var saved = _scanRepo.LoadScanResults();
        if (saved.Count > 0)
            _jobs.ScanJob = new(false, _L["Scan_Done"], saved.Count, saved.Count,
                saved.Select(r => new ScanResult(r.appId, r.name, r.cached, r.error)).ToList());

        var depotCounts = _cacheRepo.GetCacheFileCountsByDepot();
        if (depotCounts.Count > 0 && _cacheBrowser != null)
        {
            var depotAppMap = _appRepo.GetDepotAppMap();
            var appChunks = new Dictionary<uint, (string? name, int chunks)>();
            foreach (var (depotId, count) in depotCounts)
                if (depotAppMap.TryGetValue(depotId, out var info))
                {
                    if (appChunks.TryGetValue(info.appId, out var existing))
                        appChunks[info.appId] = (existing.name ?? info.name, existing.chunks + count);
                    else
                        appChunks[info.appId] = (info.name, count);
                }
            _jobs.CachedGames = appChunks
                .Select(kv => new CachedGameInfo(kv.Key, kv.Value.name ?? $"App {kv.Key}", kv.Value.chunks))
                .OrderByDescending(g => g.ChunkCount).ToList();
            _log.LogInformation("Cache browser restored: {Games} games", _jobs.CachedGames.Count);
        }
    }

    public bool HasPreviousScan() => _scanRepo.HasPreviousScan();
    public void ResetScan() => _jobs.ScanJob = new(false, "idle", 0, 0, []);

    public bool StartReconcileJob(IEnumerable<uint> appIds, int concurrency = 4)
    {
        if (!_jobs.JobLock.Wait(0)) return false;
        _jobs.ActiveJob = "scan";
        _jobs.ScanCts = new CancellationTokenSource();
        var ct = _jobs.ScanCts.Token;
        List<uint> idList;
        try { idList = appIds.ToList(); }
        catch { _jobs.ScanCts = null; _jobs.ActiveJob = null; _jobs.JobLock.Release(); throw; }

        _ = Task.Run(async () =>
        {
            try
            {
                _jobs.ScanJob = new(true, _L["Scan_LoadingCacheIndex"], 0, 0, []);
                var cacheIndex = _cacheRepo.GetStoredCacheHashes();
                if (cacheIndex.Count == 0) { FinishScan(_L["Error_NoCacheData"], []); return; }

                _jobs.ScanJob = new(true, _L["Scan_LoadingMetadata"], 0, 0, []);
                var apps = await _appInfo.GetAppInfoAsync(idList, skipOwnershipCheck: true);
                PopulateDepotMap(apps);

                var resultList = new List<ScanResult>();
                var resultLock = new object();
                int done = 0;
                await Parallel.ForEachAsync(apps,
                    new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = ct },
                    async (app, token) =>
                    {
                        var idx = Interlocked.Increment(ref done);
                        _jobs.ScanJob = _jobs.ScanJob with { Done = idx - 1, Total = apps.Count,
                            Status = string.Format(_L["Scan_AppProgress"], idx, apps.Count, app.Name) };
                        ScanResult? scanResult = null;
                        try { scanResult = await VerifyAppCacheAsync(app, cacheIndex, token); }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                        catch (Exception ex) { scanResult = new ScanResult(app.AppId, app.Name, false, ex.Message); }
                        if (scanResult != null)
                        {
                            lock (resultLock) { resultList.Add(scanResult); _jobs.ScanJob = _jobs.ScanJob with { Results = resultList.ToList() }; }
                            _scanRepo.UpsertScanResult(scanResult.AppId, scanResult.Name, scanResult.Cached);
                        }
                    });

                await ResolveCacheBrowser(apps, resultList, ct);
                FinishScan(ct.IsCancellationRequested ? _L["Scan_Cancelled"] : _L["Scan_Done"], resultList);
            }
            catch (OperationCanceledException)
            {
                FinishScan(_L["Scan_Cancelled"], []);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Reconcile failed");
                _jobs.ScanJob = _jobs.ScanJob with { Running = false, Status = $"Error: {ex.Message}" };
            }
            finally { _jobs.ScanCts = null; _jobs.ActiveJob = null; _jobs.JobLock.Release(); }
        });
        return true;
    }

    public bool StartScanJob(IEnumerable<uint> appIds, bool deep = false, int concurrency = 4)
    {
        if (!_jobs.JobLock.Wait(0)) return false;
        _jobs.ActiveJob = "scan";
        _jobs.ScanCts = new CancellationTokenSource();
        var ct = _jobs.ScanCts.Token;
        List<uint> idList;
        try { idList = appIds.ToList(); }
        catch { _jobs.ScanCts = null; _jobs.ActiveJob = null; _jobs.JobLock.Release(); throw; }

        _ = Task.Run(async () =>
        {
            try
            {
                var cacheDir = Environment.GetEnvironmentVariable("LANCACHE_CACHE_DIR");
                if (string.IsNullOrEmpty(cacheDir) || !Directory.Exists(cacheDir))
                {
                    _jobs.ScanJob = new(false, _L["Error_NoCacheDir"], 0, 0, []);
                    return;
                }

                // Phase 1: Walk filesystem
                _jobs.ScanJob = new(true, _L["Scan_ScanningDir"], 0, 256, []);
                var currentHashes = new HashSet<string>();
                var dirs = Directory.GetDirectories(cacheDir);
                int dirsProcessed = 0;
                foreach (var dir1 in dirs)
                {
                    foreach (var dir2 in Directory.EnumerateDirectories(dir1))
                        foreach (var file in Directory.EnumerateFiles(dir2))
                            currentHashes.Add(Path.GetFileName(file));
                    dirsProcessed++;
                    if (ct.IsCancellationRequested) break;
                    _jobs.ScanJob = new(true, string.Format(_L["Scan_ScanningDirProgress"], dirsProcessed, dirs.Length), dirsProcessed, dirs.Length, []);
                }
                if (ct.IsCancellationRequested) { FinishScan(_L["Scan_Cancelled"], []); return; }

                // Phase 2: Diff
                if (deep) _scanRepo.SaveScanResults([]);
                var storedHashes = deep ? new HashSet<string>() : _cacheRepo.GetStoredCacheHashes();
                var newHashes = new HashSet<string>(currentHashes); newHashes.ExceptWith(storedHashes);
                var evictedHashes = new HashSet<string>(storedHashes); evictedHashes.ExceptWith(currentHashes);
                _log.LogInformation("Cache diff: {Current} current, {Stored} stored, {New} new, {Evicted} evicted",
                    currentHashes.Count, storedHashes.Count, newHashes.Count, evictedHashes.Count);

                // Phase 3: Read new files
                _jobs.ScanJob = new(true, string.Format(_L["Scan_ReadingNewEntries"], newHashes.Count.ToString("N0")), 0, 0, []);
                var newEntries = new List<(string hash, uint depotId)>();
                var newDepotIds = new HashSet<uint>();
                var keyRegex = new Regex(@"KEY: steam/depot/(\d+)/chunk/", RegexOptions.Compiled);
                int readCount = 0;
                if (newHashes.Count > 0)
                {
                    if (deep) _cacheRepo.ClearCacheFiles();
                    foreach (var dir1 in Directory.EnumerateDirectories(cacheDir))
                    {
                        if (ct.IsCancellationRequested) break;
                        foreach (var dir2 in Directory.EnumerateDirectories(dir1))
                        {
                            if (ct.IsCancellationRequested) break;
                            foreach (var file in Directory.EnumerateFiles(dir2))
                            {
                                if (ct.IsCancellationRequested) break;
                                var fname = Path.GetFileName(file);
                                if (!newHashes.Contains(fname)) continue;
                                try
                                {
                                    using var fs = File.OpenRead(file);
                                    var size = (int)Math.Min(fs.Length, 4096);
                                    var buf = System.Buffers.ArrayPool<byte>.Shared.Rent(size);
                                    try
                                    {
                                        var read = fs.Read(buf, 0, size);
                                        var text = Encoding.ASCII.GetString(buf, 0, read);
                                        var match = keyRegex.Match(text);
                                        if (match.Success && uint.TryParse(match.Groups[1].Value, out var depotId))
                                        {
                                            newEntries.Add((fname, depotId));
                                            newDepotIds.Add(depotId);
                                        }
                                    }
                                    finally { System.Buffers.ArrayPool<byte>.Shared.Return(buf); }
                                }
                                catch { }
                                readCount++;
                                if (readCount % 1000 == 0)
                                    _jobs.ScanJob = new(true, string.Format(_L["Scan_ReadingProgress"], readCount.ToString("N0"), newHashes.Count.ToString("N0")), readCount, newHashes.Count, []);
                                if (readCount % 5000 == 0 && newEntries.Count > 0)
                                {
                                    _cacheRepo.InsertCacheFiles(newEntries);
                                    newEntries.Clear();
                                }
                            }
                        }
                    }
                    _cacheRepo.InsertCacheFiles(newEntries);
                }

                if (ct.IsCancellationRequested) { FinishScan(_L["Scan_Cancelled"], []); return; }

                // Phase 4: Evictions
                List<uint> affectedDepots = [];
                if (evictedHashes.Count > 0)
                {
                    _jobs.ScanJob = new(true, _L["Scan_ProcessingEvictions"], 0, 0, []);
                    affectedDepots = _cacheRepo.DeleteEvictedCacheFiles(evictedHashes);
                }

                // Phase 5: Verify
                _jobs.ScanJob = new(true, _L["Scan_LoadingMetadata"], 0, 0, []);
                var apps = await _appInfo.GetAppInfoAsync(idList, skipOwnershipCheck: true);
                PopulateDepotMap(apps);

                var depotCounts = _cacheRepo.GetCacheFileCountsByDepot();
                var resultList = new List<ScanResult>();
                var resultLock = new object();
                var previousResults = deep ? new Dictionary<uint, ScanResult>()
                    : _jobs.ScanJob.Results.ToDictionary(r => r.AppId);
                _jobs.ScanJob = new(true, _L["Scan_CheckingStatus"], 0, apps.Count, []);

                var changedDepots = deep ? null : new HashSet<uint>(affectedDepots.Concat(newDepotIds));
                var appsToVerify = deep ? apps
                    : apps.Where(a => a.Depots.Any(d => changedDepots!.Contains(d.DepotId))
                        || !previousResults.ContainsKey(a.AppId)).ToList();

                int scanDone = 0;
                await Parallel.ForEachAsync(appsToVerify,
                    new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = ct },
                    async (app, token) =>
                    {
                        var idx = Interlocked.Increment(ref scanDone);
                        _jobs.ScanJob = _jobs.ScanJob with { Done = idx - 1, Total = appsToVerify.Count,
                            Status = string.Format(_L["Scan_AppProgress"], idx, appsToVerify.Count, app.Name) };
                        ScanResult? scanResult = null;
                        try { scanResult = await VerifyAppCacheAsync(app, currentHashes, token); }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                        catch (Exception ex) { scanResult = new ScanResult(app.AppId, app.Name, false, ex.Message); }
                        if (scanResult != null)
                        {
                            lock (resultLock) { resultList.Add(scanResult); _jobs.ScanJob = _jobs.ScanJob with { Results = resultList.ToList() }; }
                            _scanRepo.UpsertScanResult(scanResult.AppId, scanResult.Name, scanResult.Cached);
                        }
                    });

                if (!deep)
                {
                    var verified = new HashSet<uint>(resultList.Select(r => r.AppId));
                    foreach (var app in apps.Where(a => !verified.Contains(a.AppId)))
                        resultList.Add(previousResults.TryGetValue(app.AppId, out var prev)
                            ? prev : new ScanResult(app.AppId, app.Name, false, null));
                }

                await ResolveCacheBrowser(apps, resultList, ct);
                FinishScan(ct.IsCancellationRequested ? _L["Scan_Cancelled"] : _L["Scan_Done"], resultList);
            }
            catch (OperationCanceledException)
            {
                FinishScan(_L["Scan_Cancelled"], []);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Scan job failed");
                _jobs.ScanJob = _jobs.ScanJob with { Running = false, Status = $"Error: {ex.Message}" };
            }
            finally { _jobs.ScanCts = null; _jobs.ActiveJob = null; _jobs.JobLock.Release(); }
        });
        return true;
    }

    private void PopulateDepotMap(List<AppState> apps)
    {
        _cacheBrowser?.PopulateMapFromOwnedApps(
            apps.Select(a => (a.AppId, (string?)a.Name, a.Depots.Select(d => d.DepotId))));
    }

    private async Task ResolveCacheBrowser(List<AppState> apps, List<ScanResult> resultList, CancellationToken ct)
    {
        var depotCounts = _cacheRepo.GetCacheFileCountsByDepot();
        var ownedDepotIds = new HashSet<uint>(apps.SelectMany(a => a.Depots.Select(d => d.DepotId)));
        var unknownDepotCounts = depotCounts
            .Where(kv => !ownedDepotIds.Contains(kv.Key) && kv.Value > 0)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        if (_cacheBrowser != null && !ct.IsCancellationRequested && unknownDepotCounts.Count > 0)
        {
            _jobs.ScanJob = _jobs.ScanJob with { Status = _L["Scan_ResolvingDepots"] };
            try
            {
                var cachedOwned = resultList.Where(r => r.Cached)
                    .Select(r => new CachedGameInfo(r.AppId, r.Name, 0)).ToList();
                var unknownResolved = await _cacheBrowser.ResolveDepotsAsync(unknownDepotCounts, ct);
                _jobs.CachedGames = cachedOwned.Concat(unknownResolved).OrderByDescending(g => g.ChunkCount).ToList();
            }
            catch (Exception ex) { _log.LogWarning(ex, "Cache browser resolution failed"); }
        }
        else
            _jobs.CachedGames = resultList.Where(r => r.Cached)
                .Select(r => new CachedGameInfo(r.AppId, r.Name, 0)).ToList();
    }

    private void FinishScan(string status, List<ScanResult> results)
    {
        _jobs.ScanJob = new(false, status, results.Count, results.Count, results);
        if (results.Count > 0)
            _scanRepo.SaveScanResults(results.Select(r => (r.AppId, r.Name, r.Cached, r.Error)));
        _log.LogInformation("Scan {Status}: {Count} results", status, results.Count);
    }

    public async Task<ScanResult> VerifyAppCacheAsync(AppState app, HashSet<string> cacheFileIndex, CancellationToken ct)
    {
        if (app.Depots.Count == 0) return new(app.AppId, app.Name, false, _L["Error_NoDepots"]);

        var allChunks = new List<DownloadChunk>();
        foreach (var depot in app.Depots)
        {
            ct.ThrowIfCancellationRequested();
            try { allChunks.AddRange(await _downloader.GetManifestChunksAsync(depot)); }
            catch { }
        }
        if (allChunks.Count == 0) return new(app.AppId, app.Name, false, _L["Error_NoAccessibleDepots"]);

        int hits = 0, misses = 0;
        foreach (var chunk in allChunks)
        {
            ct.ThrowIfCancellationRequested();
            var key = $"steam/depot/{chunk.DepotId}/chunk/{chunk.ChunkId.ToLowerInvariant()}bytes=0-1048575";
            var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
            if (cacheFileIndex.Contains(hash)) hits++;
            else { misses++; break; }
        }

        bool cached = hits > 0 && misses == 0;
        if (!cached)
        {
            // Clear stale downloaded_depots so IsAppUpToDate returns false
            _appRepo.ClearDownloadedDepots(app.Depots.Select(d => d.DepotId));

            if (hits > 0)
                _appRepo.MarkPartial(app.AppId);  // Some chunks present but not all
            else
                _appRepo.MarkEvicted(app.AppId);  // No chunks at all
        }
        else
        {
            _appRepo.MarkActive(app.AppId);
            _appRepo.MarkDepotsDownloaded(app.Depots);
        }
        return new ScanResult(app.AppId, app.Name, cached, null);
    }

    public async Task<bool> PreemptPrefillAsync()
    {
        if (_jobs.ActiveJob != "prefill") return _jobs.ActiveJob == null;
        _log.LogInformation("Preempting prefill for scan");
        _jobs.PrefillCts?.Cancel();
        _jobs.ClearQueue();
        for (int i = 0; i < 60 && _jobs.ActiveJob != null; i++)
            await Task.Delay(500);
        return _jobs.ActiveJob == null;
    }
}
