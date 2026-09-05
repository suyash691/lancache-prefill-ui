using LancachePrefill.Data.Repositories;

namespace LancachePrefill.Services;

/// <summary>Resolves the effective cron for a scheduler: UI-saved setting → env var → default.</summary>
internal static class SchedulerCron
{
    public static Cronos.CronExpression Resolve(ISettingsRepository settings, string settingKey,
        string envVar, string fallback, ILogger log)
    {
        var expr = settings.GetSetting(settingKey);
        if (string.IsNullOrWhiteSpace(expr))
            expr = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(expr))
            expr = fallback;
        try { return Cronos.CronExpression.Parse(expr); }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Invalid cron '{Expr}' for {Key} — using default '{Fallback}'",
                expr, settingKey, fallback);
            return Cronos.CronExpression.Parse(fallback);
        }
    }

    /// <summary>
    /// Sleeps until the next occurrence, in slices of at most one hour so that
    /// schedule edits made in the UI take effect without a restart (and so
    /// Task.Delay never sees an out-of-range duration for sparse crons).
    /// Returns true when the occurrence time has arrived; false to re-resolve.
    /// </summary>
    public static async Task<bool> WaitForNextAsync(Cronos.CronExpression cron, string what,
        ILogger log, CancellationToken ct)
    {
        var next = cron.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Local);
        if (next == null)
        {
            await Task.Delay(TimeSpan.FromHours(1), ct);
            return false;
        }
        var delay = next.Value - DateTime.UtcNow;
        if (delay > TimeSpan.FromHours(1))
        {
            log.LogInformation("Next {What}: {Next}", what, next.Value.ToLocalTime());
            await Task.Delay(TimeSpan.FromHours(1), ct);
            return false; // re-resolve: the schedule may have been edited in the UI
        }
        if (delay > TimeSpan.Zero)
        {
            log.LogInformation("Next {What}: {Next}", what, next.Value.ToLocalTime());
            await Task.Delay(delay, ct);
        }
        return true;
    }
}

public class PrefillScheduler : BackgroundService
{
    private readonly PrefillService _prefill;
    private readonly JobCoordinator _jobs;
    private readonly SteamSession _session;
    private readonly ISettingsRepository _settings;
    private readonly ILogger<PrefillScheduler> _log;

    public PrefillScheduler(PrefillService prefill, JobCoordinator jobs,
        SteamSession session, ISettingsRepository settings, ILogger<PrefillScheduler> log)
    {
        _prefill = prefill;
        _jobs = jobs;
        _session = session;
        _settings = settings;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var cron = SchedulerCron.Resolve(_settings, "prefill_schedule",
                    "PREFILL_SCHEDULE", "0 4 * * *", _log);
                if (!await SchedulerCron.WaitForNextAsync(cron, "prefill", _log, ct)) continue;

                if (!await _session.EnsureConnectedAsync())
                {
                    _log.LogWarning("Skipping scheduled prefill — no Steam session " +
                        "(log in once via the web UI to store a refresh token)");
                    continue;
                }
                for (int i = 0; i < 30 && _jobs.ActiveJob != null; i++)
                    await Task.Delay(10_000, ct);
                if (ct.IsCancellationRequested) break;
                if (_jobs.ActiveJob == null)
                    await _prefill.RunPrefillAsync(ct: ct);
                else
                    _log.LogWarning("Skipping scheduled prefill — {Job} is running", _jobs.ActiveJob);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Never let a scheduler exception escape: BackgroundService's default
                // behavior would stop the entire host.
                _log.LogError(ex, "Prefill scheduler iteration failed");
                try { await Task.Delay(TimeSpan.FromMinutes(1), ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}

public class ScanScheduler : BackgroundService
{
    private readonly ScanService _scan;
    private readonly JobCoordinator _jobs;
    private readonly SteamSession _session;
    private readonly IAppRepository _appRepo;
    private readonly ISettingsRepository _settings;
    private readonly ILogger<ScanScheduler> _log;

    public ScanScheduler(ScanService scan, JobCoordinator jobs,
        SteamSession session, IAppRepository appRepo, ISettingsRepository settings,
        ILogger<ScanScheduler> log)
    {
        _scan = scan;
        _jobs = jobs;
        _session = session;
        _appRepo = appRepo;
        _settings = settings;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var cron = SchedulerCron.Resolve(_settings, "scan_schedule",
                    "SCAN_SCHEDULE", "0 3 */3 * *", _log);
                if (!await SchedulerCron.WaitForNextAsync(cron, "scan", _log, ct)) continue;

                if (!await _session.EnsureConnectedAsync())
                {
                    _log.LogWarning("Skipping scheduled scan — no Steam session " +
                        "(log in once via the web UI to store a refresh token)");
                    continue;
                }

                if (_jobs.ActiveJob == "prefill")
                {
                    _log.LogInformation("Scan preempting running prefill");
                    await _scan.PreemptPrefillAsync();
                }

                if (_jobs.ActiveJob == null)
                {
                    var scanIds = _session.OwnedAppIds.Union(_appRepo.GetSelectedApps().Select(x => (uint)x));
                    // Note: no prefill is chained here. The scan clears downloaded_depots for
                    // evicted apps, and the separate prefill schedule picks those up — chaining
                    // both back-to-back doubled the I/O load on the cache host.
                    _scan.StartScanJob(scanIds, deep: false);
                }
                else
                    _log.LogWarning("Skipping scheduled scan — {Job} still running after preempt", _jobs.ActiveJob);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Scan scheduler iteration failed");
                try { await Task.Delay(TimeSpan.FromMinutes(1), ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
