using LancachePrefill.Data.Repositories;
using Microsoft.Extensions.Localization;

namespace LancachePrefill.Services;

public class PrefillService
{
    private readonly IAppInfoProvider _appInfo;
    private readonly IDepotDownloader _downloader;
    private readonly IAppRepository _appRepo;
    private readonly IScanRepository _scanRepo;
    private readonly ISettingsRepository _settings;
    private readonly IRunHistoryRepository? _history;
    private readonly JobCoordinator _jobs;
    private readonly ILogger<PrefillService> _log;
    private readonly IStringLocalizer<Messages> _L;

    public PrefillService(IAppInfoProvider appInfo, IDepotDownloader downloader,
        IAppRepository appRepo, IScanRepository scanRepo, ISettingsRepository settings,
        JobCoordinator jobs, ILogger<PrefillService> log, IStringLocalizer<Messages> L,
        IRunHistoryRepository? history = null)
    {
        _appInfo = appInfo;
        _downloader = downloader;
        _appRepo = appRepo;
        _scanRepo = scanRepo;
        _settings = settings;
        _history = history;
        _jobs = jobs;
        _log = log;
        _L = L;
    }

    public bool EnqueuePrefill(bool force, List<uint> appIds)
    {
        if (_jobs.ActiveJob != null && _jobs.ActiveJob != "prefill") return false;
        _jobs.AddToQueue(new QueuedSync(appIds, force));
        if (_jobs.JobLock.Wait(0))
        {
            _jobs.ActiveJob = "prefill";
            _jobs.PrefillCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
            _ = Task.Run(RunQueuedPrefillAsync);
        }
        else if (_jobs.ActiveJob != "prefill")
        {
            // Lost a race: a scan grabbed the lock between the ActiveJob check and
            // Wait(0). Nothing drains the queue while a scan runs, so undo the
            // enqueue and report busy instead of leaving an orphaned queue entry.
            // (If a finishing prefill picked the item up in this window, removing
            // it is a no-op.)
            foreach (var id in appIds) _jobs.DequeueSync(id);
            return false;
        }
        return true;
    }

    /// <summary>Drains the queue, resolves app info, and runs prefill — absorbing new queue items mid-flight.</summary>
    private async Task RunQueuedPrefillAsync()
    {
        try
        {
            // Initial drain
            var (queuedIds, force) = _jobs.DrainQueue();
            if (queuedIds.Count == 0) return;

            foreach (var id in queuedIds) _appInfo.InvalidateSingle(id);

            var apps = await _appInfo.GetAppInfoAsync(queuedIds, skipOwnershipCheck: true);
            // Queue items only ever originate from user actions (per-app Sync,
            // evicted re-cache) — record them as "manual" in run history, not the
            // internal routing detail "queued".
            await RunPrefillInternalAsync(force, apps, _jobs.PrefillCts!.Token, "manual");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.LogError(ex, "Prefill queue failed"); }
        finally
        {
            if (_jobs.Progress.Running)
                _jobs.Progress = _jobs.Progress with { Status = "cancelled", Running = false };
            _jobs.PrefillCts = null;
            _jobs.ActiveJob = null;
            _jobs.JobLock.Release();

            // Check for leftovers added during our run
            if (_jobs.GetSyncQueue().Count > 0 && _jobs.JobLock.Wait(0))
            {
                _jobs.ActiveJob = "prefill";
                _jobs.PrefillCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
                _ = Task.Run(RunQueuedPrefillAsync);
            }
        }
    }

    public async Task RunPrefillAsync(bool force = false, List<uint>? specificAppIds = null,
        CancellationToken ct = default, string trigger = "manual")
    {
        if (!_jobs.JobLock.Wait(0)) return;
        _jobs.PrefillCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _jobs.ActiveJob = "prefill";
        try
        {
            if (specificAppIds == null)
                _appInfo.InvalidateCache();
            else
                foreach (var id in specificAppIds) _appInfo.InvalidateSingle(id);

            // Include evicted apps: the scheduled scan clears their downloaded_depots
            // precisely so the next full prefill re-downloads them.
            var appIds = specificAppIds ?? _appRepo.GetAppsByStatus("active", "partial", "evicted");
            var apps = await _appInfo.GetAppInfoAsync(appIds, skipOwnershipCheck: true);
            await RunPrefillInternalAsync(force, apps, _jobs.PrefillCts.Token, trigger);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.LogError(ex, "Prefill failed"); }
        finally
        {
            if (_jobs.Progress.Running)
                _jobs.Progress = _jobs.Progress with { Status = "cancelled", Running = false };
            _jobs.PrefillCts = null;
            _jobs.ActiveJob = null;
            _jobs.JobLock.Release();
        }
    }

    private async Task RunPrefillInternalAsync(bool force, List<AppState> apps, CancellationToken token,
        string trigger = "manual")
    {
        var startedAt = DateTime.UtcNow;
        int done = 0, cached = 0, partial = 0, skipped = 0, failed = 0;
        long totalBytes = 0;
        var results = new List<AppPrefillResult>();
        var processedIds = new HashSet<uint>();
        _jobs.Progress = new("running", null, 0, apps.Count, 0, true, results);

        // Load throughput settings once per run. Conservative defaults: the lancache
        // is usually serving real clients at the same time.
        var concurrency = int.TryParse(_settings.GetSetting("prefill_concurrency"), out var pc)
            ? Math.Clamp(pc, 1, 30) : 6;
        var maxBytesPerSec = long.TryParse(_settings.GetSetting("prefill_max_mbps"), out var mbps) && mbps > 0
            ? mbps * 125_000L : 0L; // 1 Mbps = 125,000 bytes/s; 0 = unlimited

        // Helper to get pending app names
        List<string> getPending(int fromIndex) => apps.Skip(fromIndex)
            .Where(a => !processedIds.Contains(a.AppId))
            .Select(a => a.Name).ToList();

        // Records an app that needs no download and marks it processed. Returns true when skipped.
        bool recordIfNothingToDo(AppState app)
        {
            if (app.Depots.Count == 0)
            {
                results.Add(new AppPrefillResult(app.AppId, app.Name, "no_depots", 0, 0, 0, 0, [], []));
                processedIds.Add(app.AppId);
                skipped++;
                return true;
            }
            if (!force && _appRepo.IsAppUpToDate(app.Depots))
            {
                results.Add(new AppPrefillResult(app.AppId, app.Name, "skipped", 0, 0, 0, 0, [], []));
                processedIds.Add(app.AppId);
                skipped++;
                return true;
            }
            return false;
        }

        // Pre-partition: settle up-to-date / empty apps immediately so Total and
        // "Up next" only reflect apps that will actually download. Without this,
        // "Sync (2 updates)" opens a panel claiming 0/6 games with all six queued.
        var toRun = new List<AppState>();
        foreach (var app in apps)
        {
            if (processedIds.Contains(app.AppId) || toRun.Any(a => a.AppId == app.AppId)) continue;
            if (!recordIfNothingToDo(app)) toRun.Add(app);
        }
        apps = toRun;
        _jobs.Progress = new("running", null, 0, apps.Count, 0, true, results.ToList());

        int i = 0;
        while (i < apps.Count)
        {
            if (token.IsCancellationRequested) break;
            var app = apps[i];
            i++;

            // Skip duplicates (in case same app queued multiple times)
            if (!processedIds.Add(app.AppId)) continue;

            _jobs.Progress = _jobs.Progress with { CurrentApp = app.Name, Done = done, Results = results.ToList(), CurrentChunksDone = 0, CurrentChunksTotal = null, CurrentAppBytes = 0, Pending = getPending(i) };
            _log.LogInformation("Downloading {App} ({Depots} depots)", app.Name, app.Depots.Count);

            var warnings = new List<string>();
            var errors = new List<string>();

            try
            {
                // Phase 1: Get manifest chunks — only for depots that actually changed.
                // The UI's pending-size estimate is "compressed bytes of depots not at
                // the stored manifest"; downloading must honor the same boundary or a
                // one-depot patch (~314 MB) crawls every chunk of every depot (~50 GB
                // pulled through the cache). force disables the filter: verify-and-repair
                // deliberately re-walks everything.
                var storedManifests = _appRepo.GetDownloadedManifests(app.Depots.Select(d => d.DepotId));
                var depotsToFetch = force
                    ? app.Depots
                    : app.Depots.Where(d =>
                        !storedManifests.TryGetValue(d.DepotId, out var m) || m != d.ManifestId).ToList();

                if (depotsToFetch.Count < app.Depots.Count)
                    _log.LogInformation("{App}: {Changed}/{Total} depots changed — skipping {Skipped} up-to-date depots",
                        app.Name, depotsToFetch.Count, app.Depots.Count, app.Depots.Count - depotsToFetch.Count);

                var allChunks = new List<DownloadChunk>();
                var failedDepots = new HashSet<uint>();
                foreach (var depot in depotsToFetch)
                {
                    try { allChunks.AddRange(await _downloader.GetManifestChunksAsync(depot)); }
                    catch (Exception ex)
                    {
                        failedDepots.Add(depot.DepotId);
                        var depotMsg = $"Depot {depot.DepotId}: {ex.Message}";
                        warnings.Add(depotMsg);
                        _log.LogWarning("Manifest failed for {App} depot {DepotId}: {Error}", app.Name, depot.DepotId, ex.Message);
                    }
                }

                if (allChunks.Count == 0)
                {
                    errors.Add("All depot manifests failed");
                    results.Add(new AppPrefillResult(app.AppId, app.Name, "failed", 0, 0, 0, 0, warnings, errors));
                    failed++; done++;
                    _jobs.Progress = _jobs.Progress with { Done = done, Results = results.ToList() };
                    continue;
                }

                // Phase 2: Download chunks with retry
                // force also acts as verify-and-repair: cached chunks are re-pulled through
                // the cache and size-validated (mismatches are re-fetched with ?nocache=1).
                var dlResult = await _downloader.DownloadChunksWithRetryAsync(allChunks,
                    concurrency: concurrency, maxBytesPerSec: maxBytesPerSec, verifyCached: force, ct: token,
                    progress: new Progress<(long b, int d, int t)>(p =>
                        _jobs.Progress = _jobs.Progress with
                        {
                            BytesTransferred = totalBytes + p.b,
                            CurrentChunksDone = p.d,
                            CurrentChunksTotal = p.t,
                            CurrentAppBytes = p.b
                        }));

                warnings.AddRange(dlResult.Errors);
                totalBytes += dlResult.Bytes;

                // Phase 3: Determine status and update DB
                string status;
                if (dlResult.Failed == 0 && failedDepots.Count == 0)
                {
                    status = "cached";
                    cached++;

                    // Only mark depots downloaded for fully successful downloads
                    try
                    {
                        _appRepo.MarkDepotsDownloaded(app.Depots);
                        _appRepo.MarkActive(app.AppId);
                        _jobs.UpdateScanResult(app.AppId, app.Name, true, _scanRepo);
                    }
                    catch (Exception dbEx)
                    {
                        warnings.Add($"DB update failed: {dbEx.Message}");
                        _log.LogError(dbEx, "DB update failed for {App}", app.Name);
                    }
                }
                else
                {
                    status = "partial";
                    partial++;

                    try
                    {
                        // A depot whose manifest fetch failed downloaded nothing — it must
                        // NOT be stamped as downloaded (that would hide the missing content
                        // as "current" forever). When only manifests failed (all listed
                        // chunks landed), stamp the healthy depots so the next sync retries
                        // just the failed ones.
                        if (dlResult.Failed == 0 && failedDepots.Count > 0)
                            _appRepo.MarkDepotsDownloaded(app.Depots.Where(d => !failedDepots.Contains(d.DepotId)));
                        _appRepo.MarkPartial(app.AppId);
                        _jobs.UpdateScanResult(app.AppId, app.Name, false, _scanRepo);
                    }
                    catch (Exception dbEx)
                    {
                        warnings.Add($"DB update failed: {dbEx.Message}");
                        _log.LogError(dbEx, "DB update failed for {App}", app.Name);
                    }
                }

                results.Add(new AppPrefillResult(app.AppId, app.Name, status,
                    dlResult.Ok, dlResult.Failed, allChunks.Count, dlResult.Bytes, warnings, errors,
                    dlResult.CachedBytes));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed: {App}", app.Name);
                errors.Add(ex.Message);
                results.Add(new AppPrefillResult(app.AppId, app.Name, "failed", 0, 0, 0, 0, warnings, errors));
                failed++;
            }
            done++;
            _jobs.Progress = _jobs.Progress with { Done = done, BytesTransferred = totalBytes, Results = results.ToList() };

            // Absorb new queue items between apps
            var (newIds, newForce) = _jobs.DrainQueue();
            if (newIds.Count > 0)
            {
                force |= newForce;
                // Filter out already-processed or already-in-list IDs
                var existingIds = new HashSet<uint>(apps.Select(a => a.AppId));
                var trulyNew = newIds.Where(id => !existingIds.Contains(id) && !processedIds.Contains(id)).ToList();
                if (trulyNew.Count > 0)
                {
                    try
                    {
                        foreach (var id in trulyNew) _appInfo.InvalidateSingle(id);
                        var newApps = await _appInfo.GetAppInfoAsync(trulyNew, skipOwnershipCheck: true);
                        // Same partition as the initial list: settle up-to-date apps as
                        // results immediately, count only real downloads in Total.
                        var newToRun = newApps.Where(a => !recordIfNothingToDo(a)).ToList();
                        apps.AddRange(newToRun);
                        _jobs.Progress = _jobs.Progress with { Total = apps.Count, Results = results.ToList() };
                        _log.LogInformation("Absorbed {Count} queued apps into running prefill", newApps.Count);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Failed to resolve queued apps");
                    }
                }
            }
        }

        var msg = token.IsCancellationRequested
            ? string.Format(_L["Prefill_Cancelled"], cached, skipped, failed)
            : $"done: {cached} cached, {partial} partial, {skipped} current, {failed} failed";
        _jobs.Progress = new(msg, null, done, apps.Count, totalBytes, false, results);

        // Record the run in history (best effort — never fail the run over it).
        try
        {
            _history?.AddRun(new Data.Entities.PrefillRun
            {
                StartedAt = startedAt,
                FinishedAt = DateTime.UtcNow,
                Trigger = trigger,
                Status = token.IsCancellationRequested ? "cancelled" : "done",
                AppsCached = cached,
                AppsPartial = partial,
                AppsSkipped = skipped,
                AppsFailed = failed,
                Bytes = totalBytes,
                ResultsJson = System.Text.Json.JsonSerializer.Serialize(
                    results.Select(r => new { appId = r.AppId, name = r.Name, status = r.Status, bytes = r.Bytes, cachedBytes = r.CachedBytes }))
            });
        }
        catch (Exception ex) { _log.LogWarning(ex, "Failed to record prefill run history"); }
    }
}