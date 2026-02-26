using LancachePrefill.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LancachePrefill.Data.Repositories;

public class AppRepository : IAppRepository
{
    private readonly IDbContextFactory<PrefillDbContext> _factory;
    public AppRepository(IDbContextFactory<PrefillDbContext> factory) => _factory = factory;

    public List<uint> GetSelectedApps()
    {
        using var ctx = _factory.CreateDbContext();
        return ctx.SelectedApps.OrderBy(a => a.AppId).Select(a => a.AppId).ToList();
    }

    public List<uint> GetActiveApps()
    {
        using var ctx = _factory.CreateDbContext();
        return ctx.SelectedApps
            .Where(a => a.Status == "active" || a.Status == "partial")
            .OrderBy(a => a.AppId).Select(a => a.AppId).ToList();
    }

    public List<uint> GetEvictedApps()
    {
        using var ctx = _factory.CreateDbContext();
        return ctx.SelectedApps.Where(a => a.Status == "evicted")
            .OrderBy(a => a.AppId).Select(a => a.AppId).ToList();
    }

    public List<uint> GetAppsByStatus(params string[] statuses)
    {
        using var ctx = _factory.CreateDbContext();
        return ctx.SelectedApps.Where(a => statuses.Contains(a.Status))
            .OrderBy(a => a.AppId).Select(a => a.AppId).ToList();
    }

    public string GetAppStatus(uint appId)
    {
        using var ctx = _factory.CreateDbContext();
        return ctx.SelectedApps.Where(a => a.AppId == appId).Select(a => a.Status).FirstOrDefault() ?? "active";
    }

    public void AddSelectedApp(uint appId)
    {
        using var ctx = _factory.CreateDbContext();
        if (!ctx.SelectedApps.Any(a => a.AppId == appId))
        {
            ctx.SelectedApps.Add(new SelectedApp { AppId = appId });
            ctx.SaveChanges();
        }
    }

    public void RemoveSelectedApp(uint appId)
    {
        using var ctx = _factory.CreateDbContext();
        var app = ctx.SelectedApps.Find(appId);
        if (app != null) { ctx.SelectedApps.Remove(app); ctx.SaveChanges(); }
    }

    public void MarkEvicted(uint appId) => SetAppStatus(appId, "evicted");

    public void MarkActive(uint appId) => SetAppStatus(appId, "active");

    public void MarkPartial(uint appId) => SetAppStatus(appId, "partial");

    public void SetAppStatus(uint appId, string status)
    {
        using var ctx = _factory.CreateDbContext();
        var app = ctx.SelectedApps.Find(appId);
        if (app != null) { app.Status = status; ctx.SaveChanges(); }
    }

    public bool IsAppUpToDate(IEnumerable<DepotState> depots)
    {
        var list = depots.ToList();
        if (list.Count == 0) return true;
        using var ctx = _factory.CreateDbContext();
        var depotIds = list.Select(d => d.DepotId).ToList();
        var downloaded = ctx.DownloadedDepots
            .Where(dd => depotIds.Contains(dd.DepotId))
            .Select(dd => new { dd.DepotId, dd.ManifestId })
            .ToList();
        return list.All(d => downloaded.Any(dd => dd.DepotId == d.DepotId && dd.ManifestId == d.ManifestId));
    }

    public Dictionary<uint, ulong> GetDownloadedManifests(IEnumerable<uint> depotIds)
    {
        using var ctx = _factory.CreateDbContext();
        var ids = depotIds.ToList();
        return ctx.DownloadedDepots
            .Where(d => ids.Contains(d.DepotId))
            .ToDictionary(d => d.DepotId, d => d.ManifestId);
    }

    public void MarkDepotsDownloaded(IEnumerable<DepotState> depots)
    {
        using var ctx = _factory.CreateDbContext();
        foreach (var d in depots)
        {
            long did = (long)d.DepotId, mid = (long)d.ManifestId;
            ctx.Database.ExecuteSql($"DELETE FROM downloaded_depots WHERE depot_id={did} AND manifest_id!={mid}");
            ctx.Database.ExecuteSql($"INSERT OR IGNORE INTO downloaded_depots (depot_id, manifest_id) VALUES ({did}, {mid})");
        }
    }

    public void ClearDownloadedDepots(IEnumerable<uint> depotIds)
    {
        using var ctx = _factory.CreateDbContext();
        foreach (var depotId in depotIds)
        {
            long did = (long)depotId;
            ctx.Database.ExecuteSql($"DELETE FROM downloaded_depots WHERE depot_id={did}");
        }
    }

    public void StoreDepotAppMap(IEnumerable<(uint depotId, uint appId, string? name)> entries)
    {
        using var ctx = _factory.CreateDbContext();
        using var transaction = ctx.Database.BeginTransaction();
        try
        {
            foreach (var (depotId, appId, name) in entries)
            {
                long did = (long)depotId, aid = (long)appId;
                ctx.Database.ExecuteSql($"INSERT OR REPLACE INTO depot_app_map (depot_id, app_id, app_name) VALUES ({did}, {aid}, {name})");
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public Dictionary<uint, (uint appId, string? name)> GetDepotAppMap()
    {
        using var ctx = _factory.CreateDbContext();
        return ctx.DepotAppMappings.ToDictionary(d => d.DepotId, d => (d.AppId, d.AppName));
    }
}