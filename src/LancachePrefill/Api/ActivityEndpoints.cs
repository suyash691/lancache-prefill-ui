using LancachePrefill.Services;

namespace LancachePrefill.Api;

public static class ActivityEndpoints
{
    public static void MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/activity", (ActivityTracker tracker) =>
        {
            var available = AccessLogTailService.LogFilePath() != null;
            return Results.Ok(new { available, since = tracker.StartedAt, stats = available ? tracker.Snapshot() : null });
        });
    }
}
