using LancachePrefill.Services;
using Microsoft.Extensions.Localization;

namespace LancachePrefill.Api;

public static class PrefillEndpoints
{
    public static RouteGroupBuilder MapPrefillEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/prefill");

        group.MapGet("/progress", (JobCoordinator jobs) => jobs.Progress);

        group.MapPost("/", (PrefillRequest? req, PrefillService prefillService,
            JobCoordinator jobs, IStringLocalizer<Messages> L) =>
        {
            var force = req?.Force ?? false;
            var appIds = req?.AppIds;
            if (appIds != null && appIds.Count > 0)
            {
                var started = prefillService.EnqueuePrefill(force, appIds);
                return started ? Results.Ok(new { started = true })
                    : Results.Json(new { error = string.Format(L["Error_JobRunning"], jobs.ActiveJob) }, statusCode: 409);
            }
            if (jobs.ActiveJob != null) return Results.Json(new { error = string.Format(L["Error_JobRunning"], jobs.ActiveJob) }, statusCode: 409);
            _ = Task.Run(() => prefillService.RunPrefillAsync(force));
            return Results.Ok(new { started = true });
        });

        group.MapGet("/queue", (JobCoordinator jobs) =>
            jobs.GetSyncQueue().Select(q => new { appIds = q.AppIds, q.Force }));

        group.MapDelete("/queue/{appId}", (uint appId, JobCoordinator jobs) =>
            jobs.DequeueSync(appId) ? Results.Ok() : Results.NotFound());

        return group;
    }
}
