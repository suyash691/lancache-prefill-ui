using LancachePrefill.Data.Repositories;
using LancachePrefill.Services;
using Microsoft.Extensions.Localization;

namespace LancachePrefill.Api;

public static class LibraryEndpoints
{
    public static RouteGroupBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/library");

        group.MapGet("/", async (SteamSession session, AppInfoProvider? appInfoProvider,
            IAppRepository appRepo, CacheBrowserService cacheBrowser, IStringLocalizer<Messages> L,
            ILoggerFactory lf) =>
        {
            if (session.SteamId == null) return Results.Json(new { error = L["Error_NotLoggedIn"].Value }, statusCode: 401);
            try
            {
                var selected = new HashSet<uint>(appRepo.GetSelectedApps());
                var apps = await appInfoProvider!.GetAppInfoAsync(session.OwnedAppIds, skipOwnershipCheck: true);
                cacheBrowser.PopulateMapFromOwnedApps(
                    apps.Select(a => (a.AppId, (string?)a.Name, a.Depots.Select(d => d.DepotId))));
                return Results.Ok(apps.Select(a => new
                {
                    a.AppId, a.Name, depots = a.Depots.Count,
                    selected = selected.Contains(a.AppId)
                }).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex) { lf.CreateLogger("Library").LogError(ex, "Failed to load library"); return Results.Problem("Failed to load library"); }
        });

        return group;
    }
}
