using LancachePrefill.Data.Repositories;
using LancachePrefill.Services;
using Microsoft.Extensions.Localization;

namespace LancachePrefill.Api;

public static class LibraryEndpoints
{
    private static readonly HttpClient _http = new();
    private static string? _chartsJson;
    private static DateTime _chartsFetchedAt;
    private static readonly TimeSpan ChartsTtl = TimeSpan.FromMinutes(15);

    /// <summary>Parses ISteamChartsService/GetMostPlayedGames JSON into ranked app IDs.</summary>
    public static List<uint> ParseTopAppIds(string json, int n)
    {
        var ids = new List<uint>();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("response", out var resp)
            && resp.TryGetProperty("ranks", out var ranks))
            foreach (var rank in ranks.EnumerateArray())
            {
                if (rank.TryGetProperty("appid", out var appid) && appid.TryGetUInt32(out var id))
                    ids.Add(id);
                if (ids.Count >= n) break;
            }
        return ids;
    }

    public static RouteGroupBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/library");

        // Most-played games from public Steam charts — for pre-warming a cache with
        // games attendees own even when this account doesn't. Unowned apps will fail
        // at the manifest stage (Steam only grants manifest codes for owned content);
        // the UI marks them so the choice is informed.
        group.MapGet("/top", async (int? n, SteamSession session, AppInfoProvider? appInfoProvider,
            IAppRepository appRepo, IStringLocalizer<Messages> L, ILoggerFactory lf) =>
        {
            if (session.SteamId == null) return Results.Json(new { error = L["Error_NotLoggedIn"].Value }, statusCode: 401);
            var count = Math.Clamp(n ?? 50, 1, 100);
            try
            {
                if (_chartsJson == null || DateTime.UtcNow - _chartsFetchedAt > ChartsTtl)
                {
                    _chartsJson = await _http.GetStringAsync(
                        "https://api.steampowered.com/ISteamChartsService/GetMostPlayedGames/v1/");
                    _chartsFetchedAt = DateTime.UtcNow;
                }
                var ids = ParseTopAppIds(_chartsJson, count);
                var names = await appInfoProvider!.GetAppNamesAsync(ids);
                var selected = new HashSet<uint>(appRepo.GetSelectedApps());
                return Results.Ok(ids.Select((id, i) => new
                {
                    rank = i + 1,
                    appId = id,
                    name = names.GetValueOrDefault(id, $"App {id}"),
                    owned = session.OwnedAppIds.Contains(id),
                    selected = selected.Contains(id)
                }));
            }
            catch (Exception ex)
            {
                lf.CreateLogger("Library").LogError(ex, "Top games fetch failed");
                return Results.Problem("Failed to fetch Steam charts");
            }
        });

        // Apps from licenses granted in the last N days (default 14) — the
        // "--recently-purchased" equivalent, sourced from LicenseList timestamps.
        group.MapGet("/recent-purchases", (int? days, SteamSession session,
            IAppRepository appRepo, IStringLocalizer<Messages> L) =>
        {
            if (session.SteamId == null) return Results.Json(new { error = L["Error_NotLoggedIn"].Value }, statusCode: 401);
            var window = TimeSpan.FromDays(Math.Clamp(days ?? 14, 1, 90));
            var ids = session.GetRecentlyPurchasedAppIds(window);
            var selected = new HashSet<uint>(appRepo.GetSelectedApps());
            return Results.Ok(ids.Select(id => new
            {
                appId = id,
                selected = selected.Contains(id)
            }));
        });

        group.MapGet("/", async (SteamSession session, AppInfoProvider? appInfoProvider,
            IAppRepository appRepo, CacheBrowserService cacheBrowser, IStringLocalizer<Messages> L,
            ILoggerFactory lf) =>
        {
            if (session.SteamId == null) return Results.Json(new { error = L["Error_NotLoggedIn"].Value }, statusCode: 401);
            try
            {
                var selected = new HashSet<uint>(appRepo.GetSelectedApps());
                // Catch up on licenses granted since login and on package chunks that
                // failed to resolve at login — otherwise an owned game can be missing
                // from the library until the next re-login.
                await session.EnsureNewLicensesResolvedAsync();
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
