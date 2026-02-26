using SteamKit2;
using SteamKit2.CDN;

namespace LancachePrefill;

public interface IAppInfoProvider
{
    Task<List<AppState>> GetAppInfoAsync(IEnumerable<uint> appIds, bool skipOwnershipCheck = false);
    void InvalidateCache();
    void InvalidateSingle(uint appId);
}

public interface IDepotDownloader
{
    Task<List<DownloadChunk>> GetManifestChunksAsync(DepotState depot);
    Task<(int ok, int failed, long bytes)> DownloadChunksAsync(
        List<DownloadChunk> chunks, int concurrency = 30,
        IProgress<(long bytes, int done, int total)>? progress = null,
        CancellationToken ct = default);
    Task<ChunkDownloadResult> DownloadChunksWithRetryAsync(
        List<DownloadChunk> chunks, int concurrency = 30,
        IProgress<(long bytes, int done, int total)>? progress = null,
        CancellationToken ct = default);
    Task<bool?> ProbeChunkCachedAsync(DownloadChunk chunk);
}

public interface ISteamSession
{
    HashSet<uint> OwnedAppIds { get; }
    HashSet<uint> OwnedDepotIds { get; }
    SteamApps SteamApps { get; }
    SteamContent SteamContent { get; }
    Client CdnClient { get; }
}
