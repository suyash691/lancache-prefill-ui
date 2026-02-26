using SteamKit2;

namespace LancachePrefill;

public class AppInfoProvider : IAppInfoProvider
{
    private readonly ISteamSession _session;
    private readonly ILogger<AppInfoProvider> _log;
    private readonly Dictionary<uint, AppState> _cache = new();
    private static readonly HttpClient _http = new();

    public AppInfoProvider(ISteamSession session, ILogger<AppInfoProvider> log)
    {
        _session = session;
        _log = log;
    }

    public async Task<List<AppState>> GetAppInfoAsync(IEnumerable<uint> appIds, bool skipOwnershipCheck = false)
    {
        var idsToLoad = appIds.Where(id => !_cache.TryGetValue(id, out var cached) || (skipOwnershipCheck && cached.Depots.Count == 0))
            .Distinct().ToList();

        if (idsToLoad.Count > 0)
        {
            foreach (var batch in idsToLoad.Chunk(50))
            {
                var batchList = batch.ToList();
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        var tokenResponse = await _session.SteamApps.PICSGetAccessTokens(batchList, []).ToTask();
                        var requests = batchList.Select(id =>
                        {
                            var req = new SteamApps.PICSRequest(id);
                            if (tokenResponse.AppTokens.TryGetValue(id, out var token))
                                req.AccessToken = token;
                            return req;
                        }).ToList();

                        var result = await _session.SteamApps.PICSGetProductInfo(requests, []).ToTask();
                        var returnedIds = new HashSet<uint>();
                        if (result.Results != null)
                            foreach (var app in result.Results.SelectMany(r => r.Apps).Select(a => a.Value))
                            {
                                returnedIds.Add(app.ID);
                                var state = ParseAppInfo(app.ID, app.KeyValues, _session.OwnedAppIds, _session.OwnedDepotIds, skipOwnershipCheck);
                                if (state != null) _cache[app.ID] = state;
                            }

                        var missing = batchList.Where(id => !returnedIds.Contains(id)).ToList();
                        if (missing.Count > 0)
                            _log.LogWarning("PICS did not return {Count} apps: {Ids}", missing.Count, string.Join(", ", missing));
                        break;
                    }
                    catch (AsyncJobFailedException) when (attempt < 2)
                    {
                        _log.LogWarning("PICS app batch timed out, retry {N}/2", attempt + 1);
                        await Task.Delay(2000);
                    }
                }
            }

            _log.LogInformation("Loaded metadata for {Count} apps", idsToLoad.Count);
        }

        return appIds.Where(id => _cache.ContainsKey(id)).Select(id => _cache[id]).ToList();
    }

    public async Task<Dictionary<uint, string>> GetAppNamesAsync(IEnumerable<uint> appIds)
    {
        var ids = appIds.Distinct().ToList();
        if (ids.Count == 0) return new();

        var names = new Dictionary<uint, string>();
        foreach (var batch in ids.Chunk(50))
        {
            try
            {
                var tokenResponse = await _session.SteamApps.PICSGetAccessTokens(batch.ToList(), []).ToTask();
                var requests = batch.Select(id =>
                {
                    var req = new SteamApps.PICSRequest(id);
                    if (tokenResponse.AppTokens.TryGetValue(id, out var token))
                        req.AccessToken = token;
                    return req;
                }).ToList();

                var result = await _session.SteamApps.PICSGetProductInfo(requests, []).ToTask();
                if (result.Results != null)
                    foreach (var app in result.Results.SelectMany(r => r.Apps).Select(a => a.Value))
                    {
                        var name = app.KeyValues["common"]["name"].Value;
                        if (name != null) names[app.ID] = name;
                    }
            }
            catch (AsyncJobFailedException) { /* fall through to Store API */ }
        }

        // Fallback to Steam Store API for apps PICS didn't name
        var unnamed = ids.Where(id => !names.ContainsKey(id)).ToList();
        if (unnamed.Count > 0)
        {
            foreach (var id in unnamed)
            {
                try
                {
                    var json = await _http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={id}&filters=basic");
                    var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty(id.ToString(), out var entry)
                        && entry.GetProperty("success").GetBoolean()
                        && entry.GetProperty("data").TryGetProperty("name", out var n))
                        names[id] = n.GetString()!;
                }
                catch { /* best effort */ }
            }
        }

        return names;
    }

    public void InvalidateCache() => _cache.Clear();
    public void InvalidateSingle(uint appId) => _cache.Remove(appId);

    public static AppState? ParseAppInfo(uint appId, KeyValue kv, HashSet<uint> ownedAppIds, HashSet<uint> ownedDepotIds, bool skipOwnershipCheck = false)
    {
        var name = kv["common"]["name"].Value;
        var type = kv["common"]["type"].Value?.ToLowerInvariant();
        if (type is not ("game" or "beta")) return null;

        var releaseState = kv["common"]["releasestate"].Value?.ToLowerInvariant();
        if (releaseState is "unavailable" or "disabled") return null;

        var depots = new List<DepotState>();
        var depotsKv = kv["depots"];
        if (depotsKv != KeyValue.Invalid)
            foreach (var depotKv in depotsKv.Children)
            {
                if (!uint.TryParse(depotKv.Name, out var depotId)) continue;

                var manifestId = depotKv["manifests"]["public"]["gid"].AsUnsignedLong();
                if (manifestId == 0) manifestId = depotKv["manifests"]["public"].AsUnsignedLong();
                if (manifestId == 0) continue;
                if (!skipOwnershipCheck && !ownedDepotIds.Contains(depotId) && !ownedAppIds.Contains(appId)) continue;

                // Skip depots with dlcappid that we don't own
                var dlcAppId = depotKv["dlcappid"].AsUnsignedInteger();
                if (!skipOwnershipCheck && dlcAppId != 0 && !ownedAppIds.Contains(dlcAppId)) continue;

                var osList = depotKv["config"]["oslist"].Value;
                if (osList != null && !osList.Contains("windows")) continue;

                var containingAppId = depotKv["dlcappid"].AsUnsignedInteger();
                if (containingAppId == 0) containingAppId = depotKv["depotfromapp"].AsUnsignedInteger();
                if (containingAppId == 0) containingAppId = appId;

                depots.Add(new DepotState(depotId, depotKv["name"].Value, manifestId, appId, containingAppId));
            }

        return new AppState(appId, name ?? $"App {appId}", depots);
    }
}
