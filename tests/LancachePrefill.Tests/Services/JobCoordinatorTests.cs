using LancachePrefill;
using LancachePrefill.Data.Repositories;
using LancachePrefill.Services;
using NSubstitute;
using Xunit;

namespace LancachePrefill.Tests;

public class JobCoordinatorTests
{
    private readonly JobCoordinator _jobs = new();

    [Fact]
    public void InitialState_IsIdle()
    {
        Assert.Null(_jobs.ActiveJob);
        Assert.Equal("idle", _jobs.Progress.Status);
        Assert.False(_jobs.Progress.Running);
        Assert.Empty(_jobs.GetSyncQueue());
    }

    [Fact]
    public void AddToQueue_AddsItem()
    {
        _jobs.AddToQueue(new QueuedSync([730], false));
        Assert.Single(_jobs.GetSyncQueue());
        Assert.Equal(730u, _jobs.GetSyncQueue()[0].AppIds[0]);
    }

    [Fact]
    public void AddToQueue_MultipleItems()
    {
        _jobs.AddToQueue(new QueuedSync([730], false));
        _jobs.AddToQueue(new QueuedSync([440], true));
        Assert.Equal(2, _jobs.GetSyncQueue().Count);
    }

    [Fact]
    public void DrainQueue_ReturnsAllItemsMerged()
    {
        _jobs.AddToQueue(new QueuedSync([730], false));
        _jobs.AddToQueue(new QueuedSync([440], true));

        var (ids, force) = _jobs.DrainQueue();
        Assert.Equal(2, ids.Count);
        Assert.Contains(730u, ids);
        Assert.Contains(440u, ids);
        Assert.True(force); // true because second item was force
        Assert.Empty(_jobs.GetSyncQueue()); // queue drained
    }

    [Fact]
    public void DrainQueue_EmptyQueue_ReturnsEmpty()
    {
        var (ids, force) = _jobs.DrainQueue();
        Assert.Empty(ids);
        Assert.False(force);
    }

    [Fact]
    public void DrainQueue_PreservesForceFlag()
    {
        _jobs.AddToQueue(new QueuedSync([730], false));
        _jobs.AddToQueue(new QueuedSync([440], false));

        var (ids, force) = _jobs.DrainQueue();
        Assert.False(force); // both non-force
    }

    [Fact]
    public void DrainQueue_MergesMultipleAppIds()
    {
        _jobs.AddToQueue(new QueuedSync([730, 440], false));
        _jobs.AddToQueue(new QueuedSync([570], true));

        var (ids, force) = _jobs.DrainQueue();
        Assert.Equal(3, ids.Count);
        Assert.True(force);
    }

    [Fact]
    public void DequeueSync_RemovesMatchingItem()
    {
        _jobs.AddToQueue(new QueuedSync([730], false));
        _jobs.AddToQueue(new QueuedSync([440], false));

        Assert.True(_jobs.DequeueSync(730));
        Assert.Single(_jobs.GetSyncQueue());
        Assert.Equal(440u, _jobs.GetSyncQueue()[0].AppIds[0]);
    }

    [Fact]
    public void DequeueSync_NoMatch_ReturnsFalse()
    {
        _jobs.AddToQueue(new QueuedSync([730], false));
        Assert.False(_jobs.DequeueSync(999));
    }

    [Fact]
    public void CancelJob_ClearsQueueAndCancels()
    {
        _jobs.PrefillCts = new CancellationTokenSource();
        _jobs.ScanCts = new CancellationTokenSource();
        _jobs.AddToQueue(new QueuedSync([730], false));

        _jobs.CancelJob();

        Assert.Empty(_jobs.GetSyncQueue());
        Assert.True(_jobs.PrefillCts.IsCancellationRequested);
        Assert.True(_jobs.ScanCts.IsCancellationRequested);
    }

    [Fact]
    public void CancelJob_NullCts_DoesNotThrow()
    {
        _jobs.PrefillCts = null;
        _jobs.ScanCts = null;
        _jobs.CancelJob(); // Should not throw
    }

    [Fact]
    public void BumpVersion_IncrementsVersion()
    {
        var v1 = _jobs.StateVersion;
        _jobs.BumpVersion();
        Assert.Equal(v1 + 1, _jobs.StateVersion);
    }

    [Fact]
    public void GetSyncQueue_ReturnsSnapshot()
    {
        _jobs.AddToQueue(new QueuedSync([730], false));
        var snapshot = _jobs.GetSyncQueue();
        _jobs.AddToQueue(new QueuedSync([440], false));

        // Snapshot should not reflect new addition
        Assert.Single(snapshot);
        Assert.Equal(2, _jobs.GetSyncQueue().Count);
    }

    [Fact]
    public void UpdateScanResult_AddsNewResult()
    {
        var scanRepo = Substitute.For<IScanRepository>();
        _jobs.ScanJob = new ScanJobState(false, "done", 0, 0, []);

        _jobs.UpdateScanResult(730, "CS2", true, scanRepo);

        Assert.Single(_jobs.ScanJob.Results);
        Assert.True(_jobs.ScanJob.Results[0].Cached);
        scanRepo.Received(1).UpsertScanResult(730, "CS2", true);
    }

    [Fact]
    public void UpdateScanResult_UpdatesExistingResult()
    {
        var scanRepo = Substitute.For<IScanRepository>();
        _jobs.ScanJob = new ScanJobState(false, "done", 1, 1,
            [new ScanResult(730, "CS2", false, null)]);

        _jobs.UpdateScanResult(730, "CS2", true, scanRepo);

        Assert.Single(_jobs.ScanJob.Results);
        Assert.True(_jobs.ScanJob.Results[0].Cached);
    }

    [Fact]
    public void UpdateScanResult_BumpsVersion()
    {
        var scanRepo = Substitute.For<IScanRepository>();
        var v1 = _jobs.StateVersion;
        _jobs.ScanJob = new ScanJobState(false, "done", 0, 0, []);

        _jobs.UpdateScanResult(730, "CS2", true, scanRepo);

        Assert.True(_jobs.StateVersion > v1);
    }

    [Fact]
    public void Progress_SetAndGet()
    {
        var p = new PrefillProgress("running", "CS2", 1, 5, 1024, true);
        _jobs.Progress = p;
        Assert.Equal("running", _jobs.Progress.Status);
        Assert.Equal("CS2", _jobs.Progress.CurrentApp);
        Assert.True(_jobs.Progress.Running);
    }

    [Fact]
    public void CachedGames_SetAndGet()
    {
        var games = new List<CachedGameInfo> { new(730, "CS2", 100) };
        _jobs.CachedGames = games;
        Assert.Single(_jobs.CachedGames);
        Assert.Equal(730u, _jobs.CachedGames[0].AppId);
    }

    [Fact]
    public void JobLock_SingleAcquire()
    {
        Assert.True(_jobs.JobLock.Wait(0));
        Assert.False(_jobs.JobLock.Wait(0)); // Already held
        _jobs.JobLock.Release();
        Assert.True(_jobs.JobLock.Wait(0)); // Available again
        _jobs.JobLock.Release();
    }
}