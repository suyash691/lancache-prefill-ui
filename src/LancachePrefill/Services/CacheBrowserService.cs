using LancachePrefill.Data.Repositories;
using SteamKit2;

namespace LancachePrefill.Services;

public class CacheBrowserService
{
    private readonly ISteamSession _session;
    private readonly IAppRepository _appRepo;
    private readonly ILogger<CacheBrowserService> _log;

    public CacheBrowserService(ISteamSession session, IAppRepository appRepo, ILogger<CacheBrowserService> log)
    {
        _session = session;
        _appRepo = appRepo;
        _log = log;
    }

    public async Task<List<CachedGameInfo>> ResolveDepotsAsync(
        Dictionary<uint, int> depotChunkCounts, CancellationToken ct = default)
    {
        var knownMap = _appRepo.GetDepotAppMap();
        var unknownDepots = depotChunkCounts.Keys.Where(d => !knownMap.ContainsKey(d)).ToList();

        if (unknownDepots.Count > 0)
        {
            _log.LogInformation("Resolving {Count} unknown depots via PICS heuristic", unknownDepots.Count);
            await ResolveUnknownDepotsAsync(unknownDepots, ct);
            knownMap = _appRepo.GetDepotAppMap();
        }

        var appChunks = new Dictionary<uint, (string? name, int chunks)>();
        foreach (var (depotId, count) in depotChunkCounts)
            if (knownMap.TryGetValue(depotId, out var info))
            {
                if (appChunks.TryGetValue(info.appId, out var existing))
                    appChunks[info.appId] = (existing.name ?? info.name, existing.chunks + count);
                else
                    appChunks[info.appId] = (info.name, count);
            }

        return appChunks
            .Select(kv => new CachedGameInfo(kv.Key, kv.Value.name ?? $"App {kv.Key}", kv.Value.chunks))
            .ToList();
    }

    private async Task ResolveUnknownDepotsAsync(List<uint> unknownDepots, CancellationToken ct)
    {
        var candidates = new HashSet<uint>();
        foreach (var depotId in unknownDepots)
            for (int offset = -50; offset <= 50; offset++)
            {
                var candidate = (long)depotId + offset;
                if (candidate > 0 && candidate <= uint.MaxValue)
                    candidates.Add((uint)candidate);
            }

        var entries = new List<(uint depotId, uint appId, string? name)>();
        foreach (var batch in candidates.Chunk(50))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var tokenResp = await _session.SteamApps.PICSGetAccessTokens(batch.ToList(), []).ToTask();
                var requests = batch.Select(id =>
                {
                    var req = new SteamApps.PICSRequest(id);
                    if (tokenResp.AppTokens.TryGetValue(id, out var token))
                        req.AccessToken = token;
                    return req;
                }).ToList();

                var result = await _session.SteamApps.PICSGetProductInfo(requests, []).ToTask();
                if (result.Results == null) continue;

                foreach (var app in result.Results.SelectMany(r => r.Apps).Select(a => a.Value))
                {
                    var name = app.KeyValues["common"]["name"].Value;
                    var depotsKv = app.KeyValues["depots"];
                    if (depotsKv == KeyValue.Invalid) continue;
                    foreach (var d in depotsKv.Children)
                        if (uint.TryParse(d.Name, out var did))
                            entries.Add((did, app.ID, name));
                }
            }
            catch (AsyncJobFailedException ex)
            {
                _log.LogWarning("PICS batch timed out: {Msg}", ex.Message);
            }
        }

        if (entries.Count > 0)
        {
            _appRepo.StoreDepotAppMap(entries);
            _log.LogInformation("Stored {Count} depot→app mappings", entries.Count);
        }
    }

    public void PopulateMapFromOwnedApps(IEnumerable<(uint appId, string? name, IEnumerable<uint> depotIds)> apps)
    {
        var entries = new List<(uint, uint, string?)>();
        foreach (var (appId, name, depotIds) in apps)
            foreach (var depotId in depotIds)
                entries.Add((depotId, appId, name));
        if (entries.Count > 0)
            _appRepo.StoreDepotAppMap(entries);
    }
}
