using LancachePrefill.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LancachePrefill.Data.Repositories;

public class ScanRepository : IScanRepository
{
    private readonly IDbContextFactory<PrefillDbContext> _factory;
    public ScanRepository(IDbContextFactory<PrefillDbContext> factory) => _factory = factory;

    public bool HasPreviousScan()
    {
        using var ctx = _factory.CreateDbContext();
        return ctx.ScanResults.Any();
    }

    public void SaveScanResults(IEnumerable<(uint appId, string name, bool cached, string? error)> results)
    {
        using var ctx = _factory.CreateDbContext();
        ctx.Database.ExecuteSqlRaw("DELETE FROM scan_results");
        foreach (var (id, name, cached, error) in results)
            ctx.ScanResults.Add(new ScanResultEntity { AppId = id, AppName = name, Cached = cached, Error = error });
        ctx.SaveChanges();
    }

    public List<(uint appId, string name, bool cached, string? error)> LoadScanResults()
    {
        using var ctx = _factory.CreateDbContext();
        return ctx.ScanResults.Select(r => ValueTuple.Create(r.AppId, r.AppName, r.Cached, r.Error)).ToList();
    }

    public void UpsertScanResult(uint appId, string name, bool cached)
    {
        using var ctx = _factory.CreateDbContext();
        var existing = ctx.ScanResults.Find(appId);
        if (existing != null) { existing.AppName = name; existing.Cached = cached; existing.Error = null; }
        else ctx.ScanResults.Add(new ScanResultEntity { AppId = appId, AppName = name, Cached = cached });
        ctx.SaveChanges();
    }
}
