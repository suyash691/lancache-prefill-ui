using LancachePrefill.Data;
using LancachePrefill.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LancachePrefill;

/// <summary>
/// Backward-compatible facade over repositories. Used by tests and as a convenience wrapper.
/// Production code should prefer injecting specific repository interfaces.
/// </summary>
public class Database : IAppRepository, ICacheRepository, IScanRepository, ISettingsRepository, IDisposable
{
    private readonly AppRepository _apps;
    private readonly CacheRepository _cache;
    private readonly ScanRepository _scan;
    private readonly SettingsRepository _settings;
    private readonly IDbContextFactory<PrefillDbContext> _factory;

    public Database(IDbContextFactory<PrefillDbContext> factory)
    {
        _factory = factory;
        _apps = new AppRepository(factory);
        _cache = new CacheRepository(factory);
        _scan = new ScanRepository(factory);
        _settings = new SettingsRepository(factory);
    }

    /// <summary>Test/standalone constructor — creates a pooled factory from a config directory path.</summary>
    public Database(string configDir)
    {
        var dbPath = Path.Combine(configDir, "lancache-prefill.db");
        var options = new DbContextOptionsBuilder<PrefillDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        _factory = new SimpleDbContextFactory(options);

        using var ctx = _factory.CreateDbContext();
        ctx.Database.Migrate();
        ctx.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL");

        _apps = new AppRepository(_factory);
        _cache = new CacheRepository(_factory);
        _scan = new ScanRepository(_factory);
        _settings = new SettingsRepository(_factory);
    }

    // IAppRepository
    public List<uint> GetSelectedApps() => _apps.GetSelectedApps();
    public List<uint> GetActiveApps() => _apps.GetActiveApps();
    public List<uint> GetEvictedApps() => _apps.GetEvictedApps();
    public List<uint> GetAppsByStatus(params string[] statuses) => _apps.GetAppsByStatus(statuses);
    public string GetAppStatus(uint appId) => _apps.GetAppStatus(appId);
    public void AddSelectedApp(uint appId) => _apps.AddSelectedApp(appId);
    public void RemoveSelectedApp(uint appId) => _apps.RemoveSelectedApp(appId);
    public void MarkEvicted(uint appId) => _apps.MarkEvicted(appId);
    public void MarkActive(uint appId) => _apps.MarkActive(appId);
    public void MarkPartial(uint appId) => _apps.MarkPartial(appId);
    public void SetAppStatus(uint appId, string status) => _apps.SetAppStatus(appId, status);
    public bool IsAppUpToDate(IEnumerable<DepotState> depots) => _apps.IsAppUpToDate(depots);
    public Dictionary<uint, ulong> GetDownloadedManifests(IEnumerable<uint> depotIds) => _apps.GetDownloadedManifests(depotIds);
    public void MarkDepotsDownloaded(IEnumerable<DepotState> depots) => _apps.MarkDepotsDownloaded(depots);
    public void ClearDownloadedDepots(IEnumerable<uint> depotIds) => _apps.ClearDownloadedDepots(depotIds);
    public void StoreDepotAppMap(IEnumerable<(uint depotId, uint appId, string? name)> entries) => _apps.StoreDepotAppMap(entries);
    public Dictionary<uint, (uint appId, string? name)> GetDepotAppMap() => _apps.GetDepotAppMap();

    // ICacheRepository
    public HashSet<string> GetStoredCacheHashes() => _cache.GetStoredCacheHashes();
    public void InsertCacheFiles(IEnumerable<(string hash, uint depotId)> files) => _cache.InsertCacheFiles(files);
    public List<uint> DeleteEvictedCacheFiles(IEnumerable<string> hashes) => _cache.DeleteEvictedCacheFiles(hashes);
    public Dictionary<uint, int> GetCacheFileCountsByDepot() => _cache.GetCacheFileCountsByDepot();
    public void ClearCacheFiles() => _cache.ClearCacheFiles();

    // IScanRepository
    public bool HasPreviousScan() => _scan.HasPreviousScan();
    public void SaveScanResults(IEnumerable<(uint appId, string name, bool cached, string? error)> results) => _scan.SaveScanResults(results);
    public List<(uint appId, string name, bool cached, string? error)> LoadScanResults() => _scan.LoadScanResults();
    public void UpsertScanResult(uint appId, string name, bool cached) => _scan.UpsertScanResult(appId, name, cached);

    // ISettingsRepository
    public string? GetSetting(string key) => _settings.GetSetting(key);
    public void SetSetting(string key, string value) => _settings.SetSetting(key, value);
    public Dictionary<string, string> GetAllSettings() => _settings.GetAllSettings();

    public void Dispose()
    {
        try
        {
            using var ctx = _factory.CreateDbContext();
            ctx.Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(TRUNCATE)");
        }
        catch { }
    }

    private class SimpleDbContextFactory : IDbContextFactory<PrefillDbContext>
    {
        private readonly DbContextOptions<PrefillDbContext> _options;
        public SimpleDbContextFactory(DbContextOptions<PrefillDbContext> options) => _options = options;
        public PrefillDbContext CreateDbContext() => new(_options);
    }
}
