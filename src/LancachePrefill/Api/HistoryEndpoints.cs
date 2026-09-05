using LancachePrefill.Data.Repositories;

namespace LancachePrefill.Api;

public static class HistoryEndpoints
{
    public static RouteGroupBuilder MapHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/history");

        group.MapGet("/", (IRunHistoryRepository historyRepo) =>
            Results.Ok(historyRepo.GetRuns(50).Select(r => new
            {
                r.Id,
                startedAt = r.StartedAt,
                finishedAt = r.FinishedAt,
                trigger = r.Trigger,
                status = r.Status,
                appsCached = r.AppsCached,
                appsPartial = r.AppsPartial,
                appsSkipped = r.AppsSkipped,
                appsFailed = r.AppsFailed,
                bytes = r.Bytes,
                results = string.IsNullOrEmpty(r.ResultsJson)
                    ? null
                    : System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(r.ResultsJson)
                        as System.Text.Json.JsonElement?
            })));

        return group;
    }
}
