using LancachePrefill.Data.Repositories;
using LancachePrefill.Services;
using Microsoft.Extensions.Localization;

namespace LancachePrefill.Api;

public static class EvictedEndpoints
{
    public static RouteGroupBuilder MapEvictedEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/evicted");

        group.MapGet("/", async (IAppRepository appRepo, AppInfoProvider? appInfoProvider,
            IStringLocalizer<Messages> L) =>
        {
            var evictedIds = appRepo.GetEvictedApps();
            if (evictedIds.Count == 0) return Results.Ok(Array.Empty<object>());
            var names = await appInfoProvider!.GetAppNamesAsync(evictedIds);
            var fallback = L["App_FallbackName"].Value;
            return Results.Ok(evictedIds.Select(id => new
            {
                appId = id,
                name = names.GetValueOrDefault(id, string.Format(fallback, id))
            }));
        });

        group.MapPost("/{appId}/recache", (uint appId,
            PrefillService prefillService, JobCoordinator jobs, IStringLocalizer<Messages> L) =>
        {
            // Don't MarkActive here — PrefillService will set the correct status after download
            var started = prefillService.EnqueuePrefill(true, [appId]);
            return started ? Results.Ok(new { started = true })
                : Results.Json(new { error = string.Format(L["Error_JobRunning"], jobs.ActiveJob) }, statusCode: 409);
        });

        group.MapDelete("/{appId}", (uint appId, IAppRepository appRepo) =>
        {
            appRepo.RemoveSelectedApp(appId); return Results.Ok();
        });

        return group;
    }
}
