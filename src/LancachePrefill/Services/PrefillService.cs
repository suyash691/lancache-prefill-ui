using LancachePrefill.Data.Repositories;
using Microsoft.Extensions.Localization;

namespace LancachePrefill.Services;

public class PrefillService
{
    private readonly IAppInfoProvider _appInfo;
    private readonly IDepotDownloader _downloader;
    private readonly IAppRepository _appRepo;
    private readonly IScanRepository _scanRepo;
    private readonly JobCoordinator _jobs;
    private readonly ILogger<PrefillService> _log;
    private readonly IStringLocalizer<Messages> _L;

    public PrefillService(IAppInfoProvider appInfo, IDepotDownloader downloader,
        IAppRepository appRepo, IScanRepository scanRepo,
        JobCoordinator jobs, ILogger<PrefillService> log, IStringLocalizer<Messages> L)
    {
        _appInfo = appInfo;
        _downloader = downloader;
        _appRepo = appRepo;
        _scanRepo = scanRepo;
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
            await RunPrefillInternalAsync(force, apps, _jobs.PrefillCts!.Token);
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
        CancellationToken ct = default)
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

            var appIds = specificAppIds ?? _appRepo.GetActiveApps();
            var apps = await _appInfo.GetAppInfoAsync(appIds, skipOwnershipCheck: true);
            await RunPrefillInternalAsync(force, apps, _jobs.PrefillCts.Token);
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

    private async Task RunPrefillInternalAsync(bool force, List<AppState> apps, CancellationToken token)
    {
        int done = 0, cached = 0, partial = 0, skipped = 0, failed = 0;
        long totalBytes = 0;
        var results = new List<AppPrefillResult>();
        var processedIds = new HashSet<uint>();
        _jobs.Progress = new("running", null, 0, apps.Count, 0, true, results);

        // Helper to get pending app names
        List<string> getPending(int fromIndex) => apps.Skip(fromIndex)
            .Where(a => !processedIds.Contains(a.AppId))
            .Select(a => a.Name).ToList();

        int i = 0;
        while (i < apps.Count)
        {
            if (token.IsCancellationRequested) break;
            var app = apps[i];
            i++;

            // Skip duplicates (in case same app queued multiple times)
            if (!processedIds.Add(app.AppId)) continue;

            // No depots
            if (app.Depots.Count == 0)
            {
                results.Add(new AppPrefillResult(app.AppId, app.Name, "no_depots", 0, 0, 0, 0, [], []));
                skipped++; done++;
                _jobs.Progress = _jobs.Progress with { Done = done, Results = results.ToList() };
                continue;
            }

            // Already up-to-date
            if (!force && _appRepo.IsAppUpToDate(app.Depots))
            {
                results.Add(new AppPrefillResult(app.AppId, app.Name, "skipped", 0, 0, 0, 0, [], []));
                skipped++; done++;
                _jobs.Progress = _jobs.Progress with { Done = done, Results = results.ToList() };
                continue;
            }

            _jobs.Progress = _jobs.Progress with { CurrentApp = app.Name, Done = done, Results = results.ToList(), CurrentChunksDone = 0, CurrentChunksTotal = null, CurrentAppBytes = 0, Pending = getPending(i) };
            _log.LogInformation("Downloading {App} ({Depots} depots)", app.Name, app.Depots.Count);

            var warnings = new List<string>();
            var errors = new List<string>();

            try
            {
                // Phase 1: Get manifest chunks (with error capture)
                var allChunks = new List<DownloadChunk>();
                foreach (var depot in app.Depots)
                {
                    try { allChunks.AddRange(await _downloader.GetManifestChunksAsync(depot)); }
                    catch (Exception ex)
                    {
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
                var dlResult = await _downloader.DownloadChunksWithRetryAsync(allChunks, ct: token,
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
                if (dlResult.Failed == 0)
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

                    // Partial: do NOT mark depots as downloaded, set status to partial
                    try
                    {
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
                    dlResult.Ok, dlResult.Failed, allChunks.Count, dlResult.Bytes, warnings, errors));
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
                        apps.AddRange(newApps);
                        _jobs.Progress = _jobs.Progress with { Total = apps.Count };
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
    }
}