using LancachePrefill.Data.Repositories;
using LancachePrefill.Services;
using Microsoft.Extensions.Localization;

namespace LancachePrefill.Api;

public static class AppEndpoints
{
    public static RouteGroupBuilder MapAppEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/apps");

        group.MapGet("/", async (IAppRepository appRepo, AppInfoProvider? appInfoProvider,
            SteamSession session, IStringLocalizer<Messages> L, ILoggerFactory lf) =>
        {
            var ids = appRepo.GetSelectedApps();
            if (session.SteamId == null || ids.Count == 0)
                return Results.Ok(ids.Select(id => new { appId = id, name = string.Format(L["App_FallbackName"].Value, id), upToDate = (bool?)null }));
            try
            {
                var apps = await appInfoProvider!.GetAppInfoAsync(ids.Select(x => (uint)x), skipOwnershipCheck: true);
                var appMap = apps.ToDictionary(a => a.AppId);
                var fallback = L["App_FallbackName"].Value;
                var needNames = ids.Select(x => (uint)x)
                    .Where(id => !appMap.ContainsKey(id) || appMap[id].Name.StartsWith("App ")).ToList();
                var extraNames = needNames.Count > 0 ? await appInfoProvider.GetAppNamesAsync(needNames) : new();
                return Results.Ok(ids.Select(id =>
                {
                    var uid = (uint)id;
                    var hasInfo = appMap.TryGetValue(uid, out var a);
                    bool? upToDate = null;
                    string? latestManifest = null, cachedManifest = null;
                    long pendingBytes = 0, totalBytes = 0;
                    if (hasInfo && a!.Depots.Count > 0)
                    {
                        upToDate = appRepo.IsAppUpToDate(a.Depots);
                        latestManifest = string.Join(", ", a.Depots.Select(d => $"{d.DepotId}:{d.ManifestId}"));
                        var downloaded = appRepo.GetDownloadedManifests(a.Depots.Select(d => d.DepotId));
                        if (downloaded.Count > 0)
                            cachedManifest = string.Join(", ", downloaded.Select(kv => $"{kv.Key}:{kv.Value}"));
                        // Size estimate straight from appinfo (no manifest fetches):
                        // pending = compressed bytes of depots not yet at the current manifest.
                        totalBytes = a.Depots.Sum(d => d.DownloadSize);
                        pendingBytes = a.Depots
                            .Where(d => !downloaded.TryGetValue(d.DepotId, out var m) || m != d.ManifestId)
                            .Sum(d => d.DownloadSize);
                    }
                    return new
                    {
                        appId = id,
                        name = hasInfo && !a!.Name.StartsWith("App ") ? a.Name
                            : extraNames.GetValueOrDefault(uid, hasInfo ? a!.Name : string.Format(fallback, id)),
                        upToDate, latestManifest, cachedManifest,
                        pendingBytes, totalBytes,
                        status = appRepo.GetAppStatus(uid)
                    };
                }));
            }
            catch (Exception ex) { lf.CreateLogger("Apps").LogError(ex, "Failed to load apps"); return Results.Problem("Failed to load apps"); }
        });

        group.MapPost("/add", (AddAppRequest req, IAppRepository appRepo, JobCoordinator jobs) =>
        {
            appRepo.AddSelectedApp(req.AppId); jobs.BumpVersion(); return Results.Ok();
        });

        group.MapDelete("/{appId}", (uint appId, IAppRepository appRepo, JobCoordinator jobs) =>
        {
            appRepo.RemoveSelectedApp(appId); jobs.BumpVersion(); return Results.Ok();
        });

        group.MapPost("/refresh", (AppInfoProvider? appInfoProvider) =>
        {
            appInfoProvider?.InvalidateCache(); return Results.Ok();
        });

        group.MapPost("/{appId}/check", async (uint appId, IAppRepository appRepo,
            AppInfoProvider? appInfoProvider, ILoggerFactory lf) =>
        {
            try
            {
                appInfoProvider?.InvalidateSingle(appId);
                var apps = await appInfoProvider!.GetAppInfoAsync([appId], skipOwnershipCheck: true);
                var a = apps.FirstOrDefault();
                if (a == null) return Results.Ok(new { appId, upToDate = (bool?)null });
                var upToDate = a.Depots.Count > 0 ? appRepo.IsAppUpToDate(a.Depots) : (bool?)null;
                return Results.Ok(new { appId, upToDate, name = a.Name });
            }
            catch (Exception ex) { lf.CreateLogger("Apps").LogError(ex, "Update check failed for {AppId}", appId); return Results.Problem("Update check failed"); }
        });

        return group;
    }
}
