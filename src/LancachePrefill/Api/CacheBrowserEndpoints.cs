using LancachePrefill.Data.Repositories;
using LancachePrefill.Services;
using Microsoft.Extensions.Localization;

namespace LancachePrefill.Api;

public static class CacheBrowserEndpoints
{
    public static RouteGroupBuilder MapCacheBrowserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cache-browser");

        group.MapGet("/", (SteamSession session, JobCoordinator jobs, IAppRepository appRepo,
            IStringLocalizer<Messages> L) =>
        {
            if (session.SteamId == null) return Results.Json(new { error = L["Error_NotLoggedIn"].Value }, statusCode: 401);
            var games = jobs.CachedGames;
            if (games.Count == 0) return Results.Ok(new { games = Array.Empty<object>(), message = L["CacheBrowser_Empty"].Value });
            var selected = new HashSet<uint>(appRepo.GetSelectedApps());
            return Results.Ok(new
            {
                games = games.Select(g => new { g.AppId, g.Name, g.ChunkCount, selected = selected.Contains(g.AppId) }),
                message = (string?)null
            });
        });

        return group;
    }
}
