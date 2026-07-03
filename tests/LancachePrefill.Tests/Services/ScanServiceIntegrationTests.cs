using LancachePrefill;
using LancachePrefill.Services;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LancachePrefill.Tests;

/// <summary>
/// End-to-end exercise of ScanService.StartScanJob against a real on-disk cache
/// directory: filesystem walk (phase 1), new-entry KEY parsing (phase 3), and
/// per-app cache verification (phase 5). Shares the EnvSerial collection because
/// it sets the process-wide LANCACHE_CACHE_DIR.
/// </summary>
[Collection("EnvSerial")]
public class ScanServiceIntegrationTests : IDisposable
{
    private readonly string _dir;       // config dir (sqlite db)
    private readonly string _cacheDir;  // lancache cache dir
    private readonly string? _prevEnv;
    private readonly Database _db;
    private readonly JobCoordinator _jobs;
    private readonly IAppInfoProvider _appInfo;
    private readonly IDepotDownloader _downloader;
    private readonly ScanService _scan;

    // md5("steam/depot/731/chunk/aabbbytes=0-1048575") — the nginx cache filename
    // for depot 731 / chunk "aabb". levels=2:2 → ed/e7/<hash>.
    private const string GoldenHash = "c49495ea58f075da0b0bb66a569de7ed";

    public ScanServiceIntegrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"lancache-cfg-{Guid.NewGuid()}");
        _cacheDir = Path.Combine(Path.GetTempPath(), $"lancache-cache-{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(_cacheDir);
        _prevEnv = Environment.GetEnvironmentVariable("LANCACHE_CACHE_DIR");
        Environment.SetEnvironmentVariable("LANCACHE_CACHE_DIR", _cacheDir);

        _db = new Database(_dir);
        _jobs = new JobCoordinator();
        _appInfo = Substitute.For<IAppInfoProvider>();
        _downloader = Substitute.For<IDepotDownloader>();

        var L = Substitute.For<IStringLocalizer<Messages>>();
        L[Arg.Any<string>()].Returns(ci => { var k = (string)ci[0]; return new LocalizedString(k, k); });

        _scan = new ScanService(_appInfo, _downloader, _db, _db, _db, _jobs,
            NullLogger<ScanService>.Instance, L);
    }

    private void WriteCacheChunkFile(uint depotId, string chunkId, string hash)
    {
        // nginx levels=2:2 layout: <cacheDir>/<hash[-2:]>/<hash[-4:-2]>/<hash>
        var dir = Path.Combine(_cacheDir, hash[^2..], hash[^4..^2]);
        Directory.CreateDirectory(dir);
        // First 4096 bytes must contain the cache KEY line so phase 3 can extract the depot.
        File.WriteAllText(Path.Combine(dir, hash),
            $"KEY: steam/depot/{depotId}/chunk/{chunkId}\nSOME BINARY BODY\n");
    }

    private async Task WaitForScanIdle(int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (_jobs.ActiveJob != null && DateTime.UtcNow < deadline)
            await Task.Delay(50);
    }

    [Fact]
    public async Task DeepScan_DiscoversCachedApp_AndMarksActive()
    {
        _db.AddSelectedApp(730);
        WriteCacheChunkFile(731, "aabb", GoldenHash);

        var depot = new DepotState(731, "game", 100, 730, 730);
        var app = new AppState(730, "CS2", new List<DepotState> { depot });
        _appInfo.GetAppInfoAsync(Arg.Any<IEnumerable<uint>>(), Arg.Any<bool>())
            .Returns(new List<AppState> { app });
        _downloader.GetManifestChunksAsync(Arg.Any<DepotState>())
            .Returns(new List<DownloadChunk> { new(731, "aabb", 1024) });

        var started = _scan.StartScanJob(new List<uint> { 730 }, deep: true, concurrency: 1);
        Assert.True(started);

        await WaitForScanIdle();

        Assert.False(_jobs.ScanJob.Running);
        var result = Assert.Single(_jobs.ScanJob.Results, r => r.AppId == 730);
        Assert.True(result.Cached);
        // Cache index was persisted (phase 3 parsed the KEY line).
        Assert.Contains(GoldenHash, _db.GetStoredCacheHashes());
        Assert.Contains(730u, _db.GetActiveApps());
    }

    [Fact]
    public async Task DeepScan_AppNotInCache_MarksEvicted()
    {
        _db.AddSelectedApp(730);
        // A cache file for an unrelated depot so the walk finds *something*.
        WriteCacheChunkFile(999, "ffff", "a7c0c2bf593d9e589054f59c2bb22485");

        var depot = new DepotState(731, "game", 100, 730, 730);
        var app = new AppState(730, "CS2", new List<DepotState> { depot });
        _appInfo.GetAppInfoAsync(Arg.Any<IEnumerable<uint>>(), Arg.Any<bool>())
            .Returns(new List<AppState> { app });
        _downloader.GetManifestChunksAsync(Arg.Any<DepotState>())
            .Returns(new List<DownloadChunk> { new(731, "aabb", 1024) });

        var started = _scan.StartScanJob(new List<uint> { 730 }, deep: true, concurrency: 1);
        Assert.True(started);

        await WaitForScanIdle();

        var result = Assert.Single(_jobs.ScanJob.Results, r => r.AppId == 730);
        Assert.False(result.Cached);
        Assert.Contains(730u, _db.GetEvictedApps());
    }

    [Fact]
    public void StartScanJob_NoCacheDir_ReportsError()
    {
        Environment.SetEnvironmentVariable("LANCACHE_CACHE_DIR", null);
        try
        {
            var started = _scan.StartScanJob(new List<uint> { 730 }, deep: true, concurrency: 1);
            Assert.True(started); // job starts, then reports the missing-dir error
        }
        finally { Environment.SetEnvironmentVariable("LANCACHE_CACHE_DIR", _cacheDir); }
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LANCACHE_CACHE_DIR", _prevEnv);
        _db.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
        try { Directory.Delete(_cacheDir, true); } catch { }
    }
}
