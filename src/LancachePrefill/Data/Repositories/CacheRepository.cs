using Microsoft.EntityFrameworkCore;

namespace LancachePrefill.Data.Repositories;

public class CacheRepository : ICacheRepository
{
    private readonly IDbContextFactory<PrefillDbContext> _factory;
    public CacheRepository(IDbContextFactory<PrefillDbContext> factory) => _factory = factory;

    public HashSet<string> GetStoredCacheHashes()
    {
        using var ctx = _factory.CreateDbContext();
        return ctx.CacheFiles.Select(c => c.FileHash).ToHashSet();
    }

    public void InsertCacheFiles(IEnumerable<(string hash, uint depotId)> files)
    {
        using var ctx = _factory.CreateDbContext();
        foreach (var (h, d) in files)
        {
            long did = (long)d;
            ctx.Database.ExecuteSql($"INSERT OR IGNORE INTO cache_files (file_hash, depot_id) VALUES ({h}, {did})");
        }
    }

    public List<uint> DeleteEvictedCacheFiles(IEnumerable<string> hashes)
    {
        using var ctx = _factory.CreateDbContext();
        var depotIds = new HashSet<uint>();
        foreach (var batch in hashes.Chunk(500))
        {
            var batchList = batch.ToList();
            var files = ctx.CacheFiles.Where(c => batchList.Contains(c.FileHash)).ToList();
            foreach (var f in files) depotIds.Add(f.DepotId);
            ctx.CacheFiles.RemoveRange(files);
        }
        ctx.SaveChanges();
        return depotIds.ToList();
    }

    public Dictionary<uint, int> GetCacheFileCountsByDepot()
    {
        using var ctx = _factory.CreateDbContext();
        return ctx.CacheFiles.GroupBy(c => c.DepotId)
            .Select(g => new { DepotId = g.Key, Count = g.Count() })
            .ToDictionary(x => x.DepotId, x => x.Count);
    }

    public void ClearCacheFiles()
    {
        using var ctx = _factory.CreateDbContext();
        ctx.Database.ExecuteSqlRaw("DELETE FROM cache_files");
    }
}
