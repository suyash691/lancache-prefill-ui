using LancachePrefill;
using LancachePrefill.Data.Repositories;
using LancachePrefill.Services;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LancachePrefill.Tests;

public class ScanServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly IAppInfoProvider _appInfo;
    private readonly IDepotDownloader _downloader;
    private readonly Database _db;
    private readonly JobCoordinator _jobs;
    private readonly ScanService _scan;
    private readonly IStringLocalizer<Messages> _L;

    public ScanServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"lancache-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);

        _appInfo = Substitute.For<IAppInfoProvider>();
        _downloader = Substitute.For<IDepotDownloader>();
        _db = new Database(_dir);
        _jobs = new JobCoordinator();

        _L = Substitute.For<IStringLocalizer<Messages>>();
        var strings = new Dictionary<string, string>
        {
            ["Scan_Done"] = "done",
            ["Scan_Cancelled"] = "cancelled",
            ["Scan_LoadingCacheIndex"] = "Loading cache index from DB...",
            ["Scan_LoadingMetadata"] = "Loading app metadata...",
            ["Scan_ScanningDir"] = "Scanning cache directory...",
            ["Scan_ScanningDirProgress"] = "Scanning cache directory... {0}/{1}",
            ["Scan_ReadingNewEntries"] = "Reading {0} new cache entries...",
            ["Scan_ReadingProgress"] = "Reading new entries... {0}/{1}",
            ["Scan_ProcessingEvictions"] = "Processing evictions...",
            ["Scan_CheckingStatus"] = "Checking game cache status...",
            ["Scan_ResolvingDepots"] = "Resolving unknown depots...",
            ["Scan_AppProgress"] = "[{0}/{1}] {2}",
            ["Scan_ClearingPrevious"] = "Clearing previous scan data...",
            ["Error_NoCacheDir"] = "LANCACHE_CACHE_DIR not configured or missing",
            ["Error_NoCacheData"] = "No cache data",
            ["Error_NoDepots"] = "No depots",
            ["Error_NoAccessibleDepots"] = "No accessible depots",
        };
        _L[Arg.Any<string>()].Returns(ci =>
        {
            var key = (string)ci[0];
            return new LocalizedString(key, strings.GetValueOrDefault(key, key));
        });

        _scan = new ScanService(_appInfo, _downloader, _db, _db, _db, _jobs,
            NullLogger<ScanService>.Instance, _L);
    }

    [Fact]
    public void HasPreviousScan_Initially_ReturnsFalse()
    {
        Assert.False(_scan.HasPreviousScan());
    }

    [Fact]
    public void HasPreviousScan_AfterSavingResults_ReturnsTrue()
    {
        _db.SaveScanResults([(730u, "CS2", true, null)]);
        Assert.True(_scan.HasPreviousScan());
    }

    [Fact]
    public void ResetScan_ClearsInMemoryState()
    {
        _jobs.ScanJob = new ScanJobState(false, "done", 5, 5,
            [new ScanResult(730, "CS2", true, null)]);
        _scan.ResetScan();
        Assert.Equal("idle", _jobs.ScanJob.Status);
        Assert.Empty(_jobs.ScanJob.Results);
    }

    [Fact]
    public void RestoreFromDb_RestoresScanResults()
    {
        _db.SaveScanResults([(730u, "CS2", true, null), (440u, "TF2", false, "timeout")]);
        _scan.RestoreFromDb();

        Assert.Equal(2, _jobs.ScanJob.Results.Count);
        Assert.False(_jobs.ScanJob.Running);
        Assert.Equal("done", _jobs.ScanJob.Status);
    }

    [Fact]
    public void RestoreFromDb_EmptyDb_KeepsIdle()
    {
        _scan.RestoreFromDb();
        Assert.Equal("idle", _jobs.ScanJob.Status);
        Assert.Empty(_jobs.ScanJob.Results);
    }

    [Fact]
    public async Task VerifyAppCacheAsync_NoDepots_ReturnsError()
    {
        var app = new AppState(730, "CS2", []);
        var result = await _scan.VerifyAppCacheAsync(app, new HashSet<string>(), CancellationToken.None);

        Assert.Equal(730u, result.AppId);
        Assert.False(result.Cached);
        Assert.Equal("No depots", result.Error);
    }

    [Fact]
    public async Task VerifyAppCacheAsync_NoAccessibleDepots_ReturnsError()
    {
        var depot = new DepotState(731, "game", 100, 730, 730);
        var app = new AppState(730, "CS2", [depot]);

        _downloader.GetManifestChunksAsync(depot).Returns(Task.FromResult(new List<DownloadChunk>()));

        var result = await _scan.VerifyAppCacheAsync(app, new HashSet<string>(), CancellationToken.None);

        Assert.False(result.Cached);
        Assert.Equal("No accessible depots", result.Error);
    }

    [Fact]
    public async Task VerifyAppCacheAsync_AllChunksCached_ReturnsTrue()
    {
        var depot = new DepotState(731, "game", 100, 730, 730);
        var app = new AppState(730, "CS2", [depot]);

        var chunks = new List<DownloadChunk> { new(731, "aabb", 1024) };
        _downloader.GetManifestChunksAsync(depot).Returns(chunks);

        // Compute the expected cache hash
        var key = "steam/depot/731/chunk/aabbbytes=0-1048575";
        var hash = Convert.ToHexString(
            System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

        var cacheIndex = new HashSet<string> { hash };
        var result = await _scan.VerifyAppCacheAsync(app, cacheIndex, CancellationToken.None);

        Assert.True(result.Cached);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task VerifyAppCacheAsync_ChunkMissing_ReturnsFalse()
    {
        var depot = new DepotState(731, "game", 100, 730, 730);
        var app = new AppState(730, "CS2", [depot]);

        var chunks = new List<DownloadChunk> { new(731, "aabb", 1024) };
        _downloader.GetManifestChunksAsync(depot).Returns(chunks);

        var result = await _scan.VerifyAppCacheAsync(app, new HashSet<string>(), CancellationToken.None);

        Assert.False(result.Cached);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task VerifyAppCacheAsync_Cancellation_ThrowsOperationCanceled()
    {
        var depot = new DepotState(731, "game", 100, 730, 730);
        var app = new AppState(730, "CS2", [depot]);

        var chunks = new List<DownloadChunk> { new(731, "aabb", 1024) };
        _downloader.GetManifestChunksAsync(depot).Returns(chunks);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _scan.VerifyAppCacheAsync(app, new HashSet<string>(), cts.Token));
    }

    [Fact]
    public async Task VerifyAppCacheAsync_MarksEvictedWhenMissing()
    {
        _db.AddSelectedApp(730);
        var depot = new DepotState(731, "game", 100, 730, 730);
        var app = new AppState(730, "CS2", [depot]);

        var chunks = new List<DownloadChunk> { new(731, "aabb", 1024) };
        _downloader.GetManifestChunksAsync(depot).Returns(chunks);

        await _scan.VerifyAppCacheAsync(app, new HashSet<string>(), CancellationToken.None);

        Assert.Contains(730u, _db.GetEvictedApps());
    }

    [Fact]
    public async Task VerifyAppCacheAsync_MarksActiveWhenCached()
    {
        _db.AddSelectedApp(730);
        _db.MarkEvicted(730);
        var depot = new DepotState(731, "game", 100, 730, 730);
        var app = new AppState(730, "CS2", [depot]);

        var chunks = new List<DownloadChunk> { new(731, "aabb", 1024) };
        _downloader.GetManifestChunksAsync(depot).Returns(chunks);

        var key = "steam/depot/731/chunk/aabbbytes=0-1048575";
        var hash = Convert.ToHexString(
            System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

        await _scan.VerifyAppCacheAsync(app, new HashSet<string> { hash }, CancellationToken.None);

        Assert.DoesNotContain(730u, _db.GetEvictedApps());
        Assert.Contains(730u, _db.GetActiveApps());
    }

    [Fact]
    public void StartReconcileJob_WhenJobRunning_ReturnsFalse()
    {
        _jobs.JobLock.Wait(0); // Acquire lock
        try
        {
            Assert.False(_scan.StartReconcileJob([730]));
        }
        finally { _jobs.JobLock.Release(); }
    }

    [Fact]
    public async Task PreemptPrefillAsync_NoPrefillRunning_ReturnsTrue()
    {
        var result = await _scan.PreemptPrefillAsync();
        Assert.True(result);
    }

    public void Dispose() { _db.Dispose(); try { Directory.Delete(_dir, true); } catch { } }
}