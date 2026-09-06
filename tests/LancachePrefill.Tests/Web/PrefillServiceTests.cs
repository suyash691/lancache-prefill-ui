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

        _service = new PrefillService(_appInfo, _downloader, _db, _db, _db, _jobs,
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
            Arg.Any<IProgress<(long, int, int)>?>(), Arg.Any<CancellationToken>(), Arg.Any<long>(), Arg.Any<bool>())
            .Returns(new ChunkDownloadResult(1, 0, 1024L, new List<string>()));

        await _service.RunPrefillAsync(force: true);
        await _downloader.Received(1).GetManifestChunksAsync(Arg.Any<DepotState>());
        Assert.Contains("cached", _jobs.Progress.Status);
    }

    [Fact]
    public async Task Prefill_PartialDownload_CountsAsPartial()
    {
        _db.AddSelectedApp(730);
        _appInfo.GetAppInfoAsync(Arg.Any<IEnumerable<uint>>(), Arg.Any<bool>())
            .Returns(new List<AppState> { new(730, "CS2", [new(731, "game", 100, 730, 730)]) });

        _downloader.GetManifestChunksAsync(Arg.Any<DepotState>())
            .Returns(new List<DownloadChunk> { new(731, "AABB", 1024) });
        _downloader.DownloadChunksWithRetryAsync(Arg.Any<List<DownloadChunk>>(), Arg.Any<int>(),
            Arg.Any<IProgress<(long, int, int)>?>(), Arg.Any<CancellationToken>(), Arg.Any<long>(), Arg.Any<bool>())
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
            Arg.Any<IProgress<(long, int, int)>?>(), Arg.Any<CancellationToken>(), Arg.Any<long>(), Arg.Any<bool>())
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
            Arg.Any<IProgress<(long, int, int)>?>(), Arg.Any<CancellationToken>(), Arg.Any<long>(), Arg.Any<bool>())
            .Returns(async _ => { await Task.Delay(500); return new ChunkDownloadResult(1, 0, 1024L, new List<string>()); });

        var t1 = _service.RunPrefillAsync(force: true);
        await Task.Delay(50);
        var t2 = _service.RunPrefillAsync(force: true);
        await Task.WhenAll(t1, t2);

        await _downloader.Received(1).GetManifestChunksAsync(Arg.Any<DepotState>());
    }

    [Fact]
    public async Task Prefill_FetchesOnlyChangedDepots()
    {
        // Two depots; only one moved to a new manifest. The sync must fetch chunks
        // for the changed depot alone — not crawl the unchanged one (the "~314 MB
        // update downloads the full game" bug).
        _db.AddSelectedApp(570);
        var unchanged = new DepotState(571, "game", 100, 570, 570);
        var changed = new DepotState(572, "vo", 200, 570, 570);
        _db.MarkDepotsDownloaded([unchanged, new DepotState(572, "vo", 150, 570, 570)]); // 572 stored at old manifest

        _appInfo.GetAppInfoAsync(Arg.Any<IEnumerable<uint>>(), Arg.Any<bool>())
            .Returns(new List<AppState> { new(570, "Dota 2", [unchanged, changed]) });

        _downloader.GetManifestChunksAsync(Arg.Any<DepotState>())
            .Returns(new List<DownloadChunk> { new(572, "AA", 1024) });
        _downloader.DownloadChunksWithRetryAsync(Arg.Any<List<DownloadChunk>>(), Arg.Any<int>(),
            Arg.Any<IProgress<(long, int, int)>?>(), Arg.Any<CancellationToken>(), Arg.Any<long>(), Arg.Any<bool>())
            .Returns(new ChunkDownloadResult(1, 0, 1024L, new List<string>()));

        await _service.RunPrefillAsync(force: false);

        await _downloader.Received(1).GetManifestChunksAsync(Arg.Is<DepotState>(d => d.DepotId == 572));
        await _downloader.DidNotReceive().GetManifestChunksAsync(Arg.Is<DepotState>(d => d.DepotId == 571));
        // Both depots recorded current after the successful delta sync.
        Assert.True(_db.IsAppUpToDate(new List<DepotState> { unchanged, changed }));
    }

    [Fact]
    public async Task Prefill_Force_FetchesAllDepotsEvenIfUnchanged()
    {
        _db.AddSelectedApp(570);
        var d1 = new DepotState(571, "game", 100, 570, 570);
        var d2 = new DepotState(572, "vo", 200, 570, 570);
        _db.MarkDepotsDownloaded([d1, d2]); // everything already current

        _appInfo.GetAppInfoAsync(Arg.Any<IEnumerable<uint>>(), Arg.Any<bool>())
            .Returns(new List<AppState> { new(570, "Dota 2", [d1, d2]) });

        _downloader.GetManifestChunksAsync(Arg.Any<DepotState>())
            .Returns(new List<DownloadChunk> { new(571, "AA", 512) });
        _downloader.DownloadChunksWithRetryAsync(Arg.Any<List<DownloadChunk>>(), Arg.Any<int>(),
            Arg.Any<IProgress<(long, int, int)>?>(), Arg.Any<CancellationToken>(), Arg.Any<long>(), Arg.Any<bool>())
            .Returns(new ChunkDownloadResult(2, 0, 1024L, new List<string>()));

        await _service.RunPrefillAsync(force: true);

        await _downloader.Received(2).GetManifestChunksAsync(Arg.Any<DepotState>());
    }

    [Fact]
    public async Task Prefill_UpToDateApps_NotCountedInProgressTotal()
    {
        // "Sync (2 updates)" must not open a panel claiming all selected apps are
        // queued: apps with nothing to download are settled as results up front
        // and excluded from Total / pending.
        _db.AddSelectedApp(730);
        _db.AddSelectedApp(570);
        var currentDepots = new List<DepotState> { new(731, "game", 100, 730, 730) };
        _db.MarkDepotsDownloaded(currentDepots);

        _appInfo.GetAppInfoAsync(Arg.Any<IEnumerable<uint>>(), Arg.Any<bool>())
            .Returns(new List<AppState>
            {
                new(730, "CS2", currentDepots),                       // up to date -> pre-skipped
                new(570, "Dota 2", [new(572, "vo", 200, 570, 570)])  // needs download
            });

        _downloader.GetManifestChunksAsync(Arg.Any<DepotState>())
            .Returns(new List<DownloadChunk> { new(572, "AA", 1024) });
        _downloader.DownloadChunksWithRetryAsync(Arg.Any<List<DownloadChunk>>(), Arg.Any<int>(),
            Arg.Any<IProgress<(long, int, int)>?>(), Arg.Any<CancellationToken>(), Arg.Any<long>(), Arg.Any<bool>())
            .Returns(new ChunkDownloadResult(1, 0, 1024L, new List<string>()));

        await _service.RunPrefillAsync(force: false);

        Assert.Equal(1, _jobs.Progress.Total);
        Assert.Equal(1, _jobs.Progress.Done);
        Assert.Contains(_jobs.Progress.Results, r => r.AppId == 730 && r.Status == "skipped");
        Assert.Contains(_jobs.Progress.Results, r => r.AppId == 570 && r.Status == "cached");
    }

    [Fact]
    public async Task Prefill_ManifestFailure_DoesNotMarkFailedDepotDownloaded()
    {
        // A depot whose manifest fetch failed downloaded nothing. Stamping it as
        // downloaded would hide the missing content as "current" forever; the app
        // must come out partial with only the healthy depot recorded.
        _db.AddSelectedApp(570);
        var okDepot = new DepotState(571, "game", 100, 570, 570);
        var badDepot = new DepotState(572, "vo", 200, 570, 570);

        _appInfo.GetAppInfoAsync(Arg.Any<IEnumerable<uint>>(), Arg.Any<bool>())
            .Returns(new List<AppState> { new(570, "Dota 2", [okDepot, badDepot]) });

        _downloader.GetManifestChunksAsync(Arg.Is<DepotState>(d => d.DepotId == 571))
            .Returns(new List<DownloadChunk> { new(571, "AA", 512) });
        _downloader.GetManifestChunksAsync(Arg.Is<DepotState>(d => d.DepotId == 572))
            .Returns<List<DownloadChunk>>(_ => throw new InvalidOperationException("No manifest code"));
        _downloader.DownloadChunksWithRetryAsync(Arg.Any<List<DownloadChunk>>(), Arg.Any<int>(),
            Arg.Any<IProgress<(long, int, int)>?>(), Arg.Any<CancellationToken>(), Arg.Any<long>(), Arg.Any<bool>())
            .Returns(new ChunkDownloadResult(1, 0, 512L, new List<string>()));

        await _service.RunPrefillAsync(force: false);

        Assert.Contains(_jobs.Progress.Results, r => r.AppId == 570 && r.Status == "partial");
        Assert.True(_db.IsAppUpToDate(new List<DepotState> { okDepot }));   // healthy depot recorded
        Assert.False(_db.IsAppUpToDate(new List<DepotState> { badDepot })); // failed depot NOT recorded
    }

    public void Dispose() { _db.Dispose(); try { Directory.Delete(_dir, true); } catch { } }
}
