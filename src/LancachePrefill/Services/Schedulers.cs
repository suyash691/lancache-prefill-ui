using LancachePrefill.Data.Repositories;

namespace LancachePrefill.Services;

public class PrefillScheduler : BackgroundService
{
    private readonly PrefillService _prefill;
    private readonly JobCoordinator _jobs;
    private readonly SteamSession _session;
    private readonly ILogger<PrefillScheduler> _log;
    private readonly Cronos.CronExpression _cron;

    public PrefillScheduler(PrefillService prefill, JobCoordinator jobs,
        SteamSession session, ILogger<PrefillScheduler> log)
    {
        _prefill = prefill;
        _jobs = jobs;
        _session = session;
        _log = log;
        _cron = Cronos.CronExpression.Parse(
            Environment.GetEnvironmentVariable("PREFILL_SCHEDULE") ?? "0 4 * * *");
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var next = _cron.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Local);
            if (next == null) break;
            var delay = next.Value - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                _log.LogInformation("Next prefill: {Next}", next.Value.ToLocalTime());
                try { await Task.Delay(delay, ct); } catch (TaskCanceledException) { break; }
            }
            if (ct.IsCancellationRequested || _session.SteamId == null) continue;
            for (int i = 0; i < 30 && _jobs.ActiveJob != null; i++)
            {
                try { await Task.Delay(10_000, ct); }
                catch (TaskCanceledException) { break; }
            }
            if (ct.IsCancellationRequested) break;
            if (_jobs.ActiveJob == null)
                await _prefill.RunPrefillAsync(ct: ct);
            else
                _log.LogWarning("Skipping scheduled prefill — {Job} is running", _jobs.ActiveJob);
        }
    }
}

public class ScanScheduler : BackgroundService
{
    private readonly ScanService _scan;
    private readonly PrefillService _prefill;
    private readonly JobCoordinator _jobs;
    private readonly SteamSession _session;
    private readonly IAppRepository _appRepo;
    private readonly ISettingsRepository _settings;
    private readonly ILogger<ScanScheduler> _log;
    private readonly Cronos.CronExpression _cron;

    public ScanScheduler(ScanService scan, PrefillService prefill, JobCoordinator jobs,
        SteamSession session, IAppRepository appRepo, ISettingsRepository settings,
        ILogger<ScanScheduler> log)
    {
        _scan = scan;
        _prefill = prefill;
        _jobs = jobs;
        _session = session;
        _appRepo = appRepo;
        _settings = settings;
        _log = log;
        _cron = Cronos.CronExpression.Parse(
            Environment.GetEnvironmentVariable("SCAN_SCHEDULE") ?? "0 3 */3 * *");
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var next = _cron.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Local);
            if (next == null) break;
            var delay = next.Value - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                _log.LogInformation("Next scan: {Next}", next.Value.ToLocalTime());
                try { await Task.Delay(delay, ct); } catch (TaskCanceledException) { break; }
            }
            if (ct.IsCancellationRequested || _session.SteamId == null) continue;

            if (_jobs.ActiveJob == "prefill")
            {
                _log.LogInformation("Scan preempting running prefill");
                await _scan.PreemptPrefillAsync();
            }

            if (_jobs.ActiveJob == null)
            {
                var scanIds = _session.OwnedAppIds.Union(_appRepo.GetSelectedApps().Select(x => (uint)x));
                _scan.StartScanJob(scanIds, deep: false);
                while (_jobs.ActiveJob == "scan" && !ct.IsCancellationRequested)
                {
                    try { await Task.Delay(5000, ct); }
                    catch (TaskCanceledException) { break; }
                }

                if (!ct.IsCancellationRequested && _session.SteamId != null)
                {
                    _log.LogInformation("Running prefill after scheduled scan");
                    await _prefill.RunPrefillAsync(ct: ct);
                }
            }
            else
                _log.LogWarning("Skipping scheduled scan — {Job} still running after preempt", _jobs.ActiveJob);
        }
    }
}
