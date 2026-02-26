using LancachePrefill;
using LancachePrefill.Data.Repositories;

using LancachePrefill.Services;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LancachePrefill.Tests;

public class PrefillServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly IAppInfoProvider _appInfo;
    private readonly IDepotDownloader _downloader;
    private readonly Database _db;
    private readonly JobCoordinator _jobs;
    private readonly PrefillService _service;

    public PrefillServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"lancache-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);

        _appInfo = Substitute.For<IAppInfoProvider>();
        _downloader = Substitute.For<IDepotDownloader>();
        _db = new Database(_dir);
        _jobs = new JobCoordinator();

        var saved = _db.LoadScanResults();
        if (saved.Count > 0)
            _jobs.ScanJob = new(false, "done", saved.Count, saved.Count,
                saved.Select(r => new ScanResult(r.appId, r.name, r.cached, r.error)).ToList());

        var localizer = Substitute.For<IStringLocalizer<Messages>>();
        var strings = new Dictionary<string, string>
        {
            ["Prefill_Done"] = "done: {0} updated, {1} current, {2} failed",
            ["Prefill_Cancelled"] = "cancelled: {0} updated, {1} current, {2} failed"
        };
        localizer[Arg.Any<string>()].Returns(ci =>
        {
            var key = (string)ci[0];
            return new LocalizedString(key, strings.GetValueOrDefault(key, key));
        });

        _service = new PrefillService(_appInfo, _downloader, _db, _db, _jobs,
            NullLogger<PrefillService>.Instance, localizer);
    }

    [Fact]
    public async Task Prefill_SkipsUpToDateApps()
    {
        _db.AddSelectedApp(730);
        var depots = new List<DepotState> { new(731, "game", 100, 730, 730) };
        _db.MarkDepotsDownloaded(depots);

        _appInfo.GetAppInfoAsync(Arg.Any<IEnumerable<uint>>(), Arg.Any<bool>())
            .Returns(new List<AppState> { new(730, "CS2", depots) });

        await _service.RunPrefillAsync(force: false);
        await _downloader.DidNotReceive().GetManifestChunksAsync(Arg.Any<DepotState>());
        Assert.Contains("current", _jobs.Progress.Status);
    }

    [Fact]
    public async Task Prefill_Force_DownloadsEvenIfUpToDate()
    {
        _db.AddSelectedApp(730);
        var depots = new List<DepotState> { new(731, "game", 100, 730, 730) };
        _db.MarkDepotsDownloaded(depots);

        _appInfo.GetAppInfoAsync(Arg.Any<IEnumerable<uint>>(), Arg.Any<bool>())
            .Returns(new List<AppState> { new(730, "CS2", depots) });

        _downloader.GetManifestChunksAsync(Arg.Any<DepotState>())
            .Returns(new List<DownloadChunk> { new(731, "AABB", 1024) });
        _downloader.DownloadChunksWithRetryAsync(Arg.Any<List<DownloadChunk>>(), Arg.Any<int>(),
            Arg.Any<IProgress<(long, int, int)>?>(), Arg.Any<CancellationToken>())
            .Returns(new ChunkDownloadResult(1, 0, 1024L, new List<string>()));

        await _service.RunPrefillAsync(force: true);
        await _downloader.Received(1).GetManifestChunksAsync(Arg.Any<DepotState>());
        Assert.Contains("cached", _jobs.Progress.Status);
    }

    [Fact]
    public async Task Prefill_DownloadFailure_CountsAsFailed()
    {
        _db.AddSelectedApp(730);
        _appInfo.GetAppInfoAsync(Arg.Any<IEnumerable<uint>>(), Arg.Any<bool>())
            .Returns(new List<AppState> { new(730, "CS2", [new(731, "game", 100, 730, 730)]) });

        _downloader.GetManifestChunksAsync(Arg.Any<DepotState>())
            .Returns(new List<DownloadChunk> { new(731, "AABB", 1024) });
        _downloader.DownloadChunksWithRetryAsync(Arg.Any<List<DownloadChunk>>(), Arg.Any<int>(),
            Arg.Any<IProgress<(long, int, int)>?>(), Arg.Any<CancellationToken>())
            .Returns(new ChunkDownloadResult(0, 1, 0L, new List<string> { "HTTP 503" }));

        await _service.RunPrefillAsync(force: true);
        Assert.Contains("partial", _jobs.Progress.Status);
    }

    [Fact]
    public async Task Prefill_SuccessfulDownload_TracksInDb()
    {
        _db.AddSelectedApp(730);
        var depots = new List<DepotState> { new(731, "game", 100, 730, 730) };
        _appInfo.GetAppInfoAsync(Arg.Any<IEnumerable<uint>>(), Arg.Any<bool>())
            .Returns(new List<AppState> { new(730, "CS2", depots) });

        _downloader.GetManifestChunksAsync(Arg.Any<DepotState>())
            .Returns(new List<DownloadChunk> { new(731, "AA", 512) });
        _downloader.DownloadChunksWithRetryAsync(Arg.Any<List<DownloadChunk>>(), Arg.Any<int>(),
            Arg.Any<IProgress<(long, int, int)>?>(), Arg.Any<CancellationToken>())
            .Returns(new ChunkDownloadResult(1, 0, 512L, new List<string>()));

        await _service.RunPrefillAsync(force: true);
        Assert.True(_db.IsAppUpToDate(depots));
    }

    [Fact]
    public async Task Prefill_ConcurrentRun_SecondIsSkipped()
    {
        _db.AddSelectedApp(730);
        _appInfo.GetAppInfoAsync(Arg.Any<IEnumerable<uint>>(), Arg.Any<bool>())
            .Returns(new List<AppState> { new(730, "CS2", [new(731, "game", 100, 730, 730)]) });

        _downloader.GetManifestChunksAsync(Arg.Any<DepotState>())
            .Returns(new List<DownloadChunk> { new(731, "AA", 1024) });
        _downloader.DownloadChunksWithRetryAsync(Arg.Any<List<DownloadChunk>>(), Arg.Any<int>(),
            Arg.Any<IProgress<(long, int, int)>?>(), Arg.Any<CancellationToken>())
            .Returns(async _ => { await Task.Delay(500); return new ChunkDownloadResult(1, 0, 1024L, new List<string>()); });

        var t1 = _service.RunPrefillAsync(force: true);
        await Task.Delay(50);
        var t2 = _service.RunPrefillAsync(force: true);
        await Task.WhenAll(t1, t2);

        await _downloader.Received(1).GetManifestChunksAsync(Arg.Any<DepotState>());
    }

    [Fact]
    public void GetProgress_InitialState_IsIdle()
    {
        Assert.Equal("idle", _jobs.Progress.Status);
        Assert.False(_jobs.Progress.Running);
    }

    [Fact]
    public void ActiveJob_InitiallyNull()
    {
        Assert.Null(_jobs.ActiveJob);
    }

    public void Dispose() { _db.Dispose(); try { Directory.Delete(_dir, true); } catch { } }
}
