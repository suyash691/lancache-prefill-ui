using LancachePrefill.Data.Repositories;
using LancachePrefill.Services;
using Microsoft.Extensions.Localization;

namespace LancachePrefill.Api;

public static class ScanEndpoints
{
    public static RouteGroupBuilder MapScanEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/scan");

        group.MapPost("/", (ScanRequest? req, SteamSession session, ScanService scanService,
            JobCoordinator jobs, IAppRepository appRepo, ISettingsRepository settingsRepo,
            IStringLocalizer<Messages> L) =>
        {
            if (session.SteamId == null) return Results.Json(new { error = L["Error_NotLoggedIn"].Value }, statusCode: 401);
            if (jobs.ActiveJob != null) return Results.Json(new { error = string.Format(L["Error_JobRunning"], jobs.ActiveJob) }, statusCode: 409);
            var deep = req?.Deep ?? false;
            var concurrency = int.TryParse(settingsRepo.GetSetting("scan_concurrency"), out var c) ? c : 4;
            var scanIds = session.OwnedAppIds.Union(appRepo.GetSelectedApps().Select(x => (uint)x));
            var started = scanService.StartScanJob(scanIds, deep, concurrency);
            return started ? Results.Ok(new { started = true })
                : Results.Json(new { error = L["Error_JobAlreadyRunning"].Value }, statusCode: 409);
        });

        group.MapGet("/has-previous", (ScanService scanService) =>
            Results.Ok(new { hasPrevious = scanService.HasPreviousScan() }));

        group.MapGet("/status", (JobCoordinator jobs, IAppRepository appRepo) =>
        {
            var state = jobs.ScanJob;
            var selected = new HashSet<uint>(appRepo.GetSelectedApps());
            return Results.Ok(new
            {
                state.Running, state.Status, state.Done, state.Total,
                results = state.Results.Select(r => new { r.AppId, r.Name, r.Cached, r.Error, selected = selected.Contains(r.AppId) })
            });
        });

        group.MapPost("/reset", (ScanService scanService) => { scanService.ResetScan(); return Results.Ok(); });

        group.MapPost("/reconcile", (SteamSession session, ScanService scanService,
            JobCoordinator jobs, IAppRepository appRepo, ISettingsRepository settingsRepo,
            IStringLocalizer<Messages> L) =>
        {
            if (session.SteamId == null) return Results.Json(new { error = L["Error_NotLoggedIn"].Value }, statusCode: 401);
            if (jobs.ActiveJob != null) return Results.Json(new { error = string.Format(L["Error_JobRunning"], jobs.ActiveJob) }, statusCode: 409);
            var concurrency = int.TryParse(settingsRepo.GetSetting("scan_concurrency"), out var c) ? c : 4;
            var scanIds = session.OwnedAppIds.Union(appRepo.GetSelectedApps().Select(x => (uint)x));
            var started = scanService.StartReconcileJob(scanIds, concurrency);
            return started ? Results.Ok(new { started = true })
                : Results.Json(new { error = L["Error_JobAlreadyRunning"].Value }, statusCode: 409);
        });

        return group;
    }
}
