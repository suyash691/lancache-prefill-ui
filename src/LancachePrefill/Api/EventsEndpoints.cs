using LancachePrefill.Data.Repositories;
using LancachePrefill.Services;

namespace LancachePrefill.Api;

public static class EventsEndpoints
{
    public static void MapEventsEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events", async (HttpContext ctx, SteamSession session,
            JobCoordinator jobs, IAppRepository appRepo) =>
        {
            var qToken = ctx.Request.Query["token"].FirstOrDefault();
            if (session.SessionToken == null || qToken != session.SessionToken)
            {
                ctx.Response.StatusCode = 401;
                return;
            }
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            var ct = ctx.RequestAborted;
            var jsonOpts = new System.Text.Json.JsonSerializerOptions
                { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
            HashSet<uint>? cachedSelected = null;
            int lastVersion = -1;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var ver = jobs.StateVersion;
                    if (cachedSelected == null || ver != lastVersion)
                    {
                        cachedSelected = new HashSet<uint>(appRepo.GetSelectedApps());
                        lastVersion = ver;
                    }
                    var scanState = jobs.ScanJob;
                    var data = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        prefill = jobs.Progress,
                        activeJob = jobs.ActiveJob,
                        syncQueue = jobs.GetSyncQueue().Select(q => new { appIds = q.AppIds, q.Force }),
                        version = ver,
                        scan = new
                        {
                            running = scanState.Running, status = scanState.Status,
                            done = scanState.Done, total = scanState.Total,
                            results = scanState.Results.Select(r => new
                            {
                                appId = r.AppId, name = r.Name, cached = r.Cached, error = r.Error,
                                selected = cachedSelected.Contains(r.AppId)
                            }).ToList()
                        }
                    }, jsonOpts);
                    await ctx.Response.WriteAsync($"data: {data}\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
                catch (Exception) when (!ct.IsCancellationRequested) { }
                await Task.Delay(1500, ct);
            }
        });
    }
}
