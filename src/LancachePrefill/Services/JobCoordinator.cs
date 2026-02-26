namespace LancachePrefill.Services;

/// <summary>Shared job state visible to SSE, UI, and both services.</summary>
public class JobCoordinator
{
    private PrefillProgress _progress = new("idle", null, 0, 0, 0, false);
    private ScanJobState _scanJob = new(false, "idle", 0, 0, []);
    private List<CachedGameInfo> _cachedGames = [];
    private readonly SemaphoreSlim _jobLock = new(1, 1);
    private readonly List<QueuedSync> _syncQueue = new();
    private readonly object _queueLock = new();
    private volatile CancellationTokenSource? _prefillCts;
    private volatile CancellationTokenSource? _scanCts;
    private volatile int _stateVersion;

    public string? ActiveJob { get; set; }
    public int StateVersion => _stateVersion;
    public void BumpVersion() => Interlocked.Increment(ref _stateVersion);

    public PrefillProgress Progress { get => _progress; set => _progress = value; }
    public ScanJobState ScanJob { get => _scanJob; set => _scanJob = value; }
    public List<CachedGameInfo> CachedGames { get => _cachedGames; set => _cachedGames = value; }
    public SemaphoreSlim JobLock => _jobLock;

    public CancellationTokenSource? PrefillCts { get => _prefillCts; set => _prefillCts = value; }
    public CancellationTokenSource? ScanCts { get => _scanCts; set => _scanCts = value; }

    public void CancelJob()
    {
        _prefillCts?.Cancel();
        _scanCts?.Cancel();
        lock (_queueLock) { _syncQueue.Clear(); }
    }

    public List<QueuedSync> GetSyncQueue() { lock (_queueLock) { return [.. _syncQueue]; } }
    public void AddToQueue(QueuedSync item) { lock (_queueLock) { _syncQueue.Add(item); } }
    public bool DequeueSync(uint appId) { lock (_queueLock) { return _syncQueue.RemoveAll(q => q.AppIds.Contains(appId)) > 0; } }
    public void ClearQueue() { lock (_queueLock) { _syncQueue.Clear(); } }

    /// <summary>Drain all queued items and return merged app IDs + force flag.</summary>
    public (List<uint> AppIds, bool Force) DrainQueue()
    {
        lock (_queueLock)
        {
            if (_syncQueue.Count == 0) return ([], false);
            var ids = new List<uint>();
            bool force = false;
            foreach (var item in _syncQueue)
            {
                ids.AddRange(item.AppIds);
                force |= item.Force;
            }
            _syncQueue.Clear();
            return (ids, force);
        }
    }

    public void UpdateScanResult(uint appId, string name, bool cached, Data.Repositories.IScanRepository scanRepo)
    {
        var results = _scanJob.Results.ToList();
        var idx = results.FindIndex(r => r.AppId == appId);
        var entry = new ScanResult(appId, name, cached, null);
        if (idx >= 0) results[idx] = entry; else results.Add(entry);
        _scanJob = _scanJob with { Results = results };
        scanRepo.UpsertScanResult(appId, name, cached);
        BumpVersion();
    }
}
