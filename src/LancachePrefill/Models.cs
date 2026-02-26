namespace LancachePrefill;

// Domain models
public record AppState(uint AppId, string Name, List<DepotState> Depots);
public record DepotState(uint DepotId, string? Name, ulong ManifestId, uint AppId, uint ContainingAppId);
public record DownloadChunk(uint DepotId, string ChunkId, uint CompressedLength);
public record ScanResult(uint AppId, string Name, bool Cached, string? Error);
public record ScanJobState(bool Running, string Status, int Done, int Total, List<ScanResult> Results);
public record QueuedSync(List<uint> AppIds, bool Force);

// Download result from DepotDownloader
public record ChunkDownloadResult(int Ok, int Failed, long Bytes, List<string> Errors);

// Per-app prefill result
public record AppPrefillResult(
    uint AppId,
    string Name,
    string Status,          // "cached", "partial", "failed", "skipped", "no_depots"
    int ChunksOk,
    int ChunksFailed,
    int ChunksTotal,
    long Bytes,
    List<string> Warnings,
    List<string> Errors
);

// Updated prefill progress with per-app results
public record PrefillProgress(
    string Status,
    string? CurrentApp,
    int Done,
    int Total,
    long BytesTransferred,
    bool Running,
    List<AppPrefillResult>? Results = null,
    int? CurrentChunksDone = null,
    int? CurrentChunksTotal = null,
    long? CurrentAppBytes = null,
    List<string>? Pending = null
);

// Cache browser
public record CachedGameInfo(uint AppId, string Name, int ChunkCount);

// API request models
record LoginRequest(string? Username, string? Password, string? TwoFactorCode, string? EmailCode);
record AddAppRequest(uint AppId);
record PrefillRequest(bool Force = false, List<uint>? AppIds = null);
record ScanRequest(bool Deep = false);
