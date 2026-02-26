namespace LancachePrefill.Data.Repositories;

public interface IAppRepository
{
    List<uint> GetSelectedApps();
    List<uint> GetActiveApps();
    List<uint> GetEvictedApps();
    List<uint> GetAppsByStatus(params string[] statuses);
    string GetAppStatus(uint appId);
    void AddSelectedApp(uint appId);
    void RemoveSelectedApp(uint appId);
    void MarkEvicted(uint appId);
    void MarkActive(uint appId);
    void MarkPartial(uint appId);
    void SetAppStatus(uint appId, string status);
    bool IsAppUpToDate(IEnumerable<DepotState> depots);
    Dictionary<uint, ulong> GetDownloadedManifests(IEnumerable<uint> depotIds);
    void MarkDepotsDownloaded(IEnumerable<DepotState> depots);
    void ClearDownloadedDepots(IEnumerable<uint> depotIds);
    void StoreDepotAppMap(IEnumerable<(uint depotId, uint appId, string? name)> entries);
    Dictionary<uint, (uint appId, string? name)> GetDepotAppMap();
}

public interface ICacheRepository
{
    HashSet<string> GetStoredCacheHashes();
    void InsertCacheFiles(IEnumerable<(string hash, uint depotId)> files);
    List<uint> DeleteEvictedCacheFiles(IEnumerable<string> hashes);
    Dictionary<uint, int> GetCacheFileCountsByDepot();
    void ClearCacheFiles();
}

public interface IScanRepository
{
    bool HasPreviousScan();
    void SaveScanResults(IEnumerable<(uint appId, string name, bool cached, string? error)> results);
    List<(uint appId, string name, bool cached, string? error)> LoadScanResults();
    void UpsertScanResult(uint appId, string name, bool cached);
}

public interface ISettingsRepository
{
    string? GetSetting(string key);
    void SetSetting(string key, string value);
    Dictionary<string, string> GetAllSettings();
}