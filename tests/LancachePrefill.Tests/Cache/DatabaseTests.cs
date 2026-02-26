using LancachePrefill;
using Xunit;

namespace LancachePrefill.Tests;

public class DatabaseTests : IDisposable
{
    private readonly string _dir;
    private readonly Database _db;

    public DatabaseTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"lancache-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
        _db = new Database(_dir);
    }

    [Fact]
    public void SelectedApps_AddAndList()
    {
        _db.AddSelectedApp(730);
        _db.AddSelectedApp(440);
        _db.AddSelectedApp(730);
        Assert.Equal(new uint[] { 440, 730 }, _db.GetSelectedApps());
    }

    [Fact]
    public void SelectedApps_Remove()
    {
        _db.AddSelectedApp(730);
        _db.RemoveSelectedApp(730);
        Assert.DoesNotContain(730u, _db.GetSelectedApps());
    }

    [Fact]
    public void DownloadedDepots_MarkAndCheck()
    {
        var depots = new[] { new DepotState(100, "test", 999, 1, 1) };
        _db.MarkDepotsDownloaded(depots);
        Assert.True(_db.IsAppUpToDate(depots));
        Assert.False(_db.IsAppUpToDate([new DepotState(100, "test", 888, 1, 1)]));
    }

    [Fact]
    public void IsAppUpToDate_AllDepotsMustMatch()
    {
        var depots = new[] { new DepotState(1, "a", 100, 1, 1), new DepotState(2, "b", 200, 1, 1) };
        Assert.False(_db.IsAppUpToDate(depots));
        _db.MarkDepotsDownloaded([depots[0]]);
        Assert.False(_db.IsAppUpToDate(depots));
        _db.MarkDepotsDownloaded([depots[1]]);
        Assert.True(_db.IsAppUpToDate(depots));
    }

    [Fact]
    public void Eviction_MarkAndQuery()
    {
        _db.AddSelectedApp(730);
        _db.AddSelectedApp(440);
        _db.MarkEvicted(730);
        Assert.Contains(730u, _db.GetEvictedApps());
        Assert.DoesNotContain(730u, _db.GetActiveApps());
        Assert.Contains(440u, _db.GetActiveApps());
    }

    [Fact]
    public void Eviction_MarkActive_Restores()
    {
        _db.AddSelectedApp(730);
        _db.MarkEvicted(730);
        _db.MarkActive(730);
        Assert.Contains(730u, _db.GetActiveApps());
        Assert.DoesNotContain(730u, _db.GetEvictedApps());
    }

    [Fact]
    public void DepotAppMap_StoreAndRetrieve()
    {
        _db.StoreDepotAppMap([(100u, 730u, "CS2"), (101u, 730u, "CS2")]);
        var map = _db.GetDepotAppMap();
        Assert.Equal(730u, map[100u].appId);
        Assert.Equal("CS2", map[100u].name);
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void ScanResults_SaveAndLoad()
    {
        _db.SaveScanResults([(730u, "CS2", true, null), (440u, "TF2", false, "timeout")]);
        var results = _db.LoadScanResults();
        Assert.Equal(2, results.Count);
        var cs2 = results.First(r => r.appId == 730);
        var tf2 = results.First(r => r.appId == 440);
        Assert.True(cs2.cached);
        Assert.Equal("timeout", tf2.error);
    }

    [Fact]
    public void Settings_SetAndGet()
    {
        _db.SetSetting("test_key", "test_value");
        Assert.Equal("test_value", _db.GetSetting("test_key"));
        Assert.Null(_db.GetSetting("nonexistent"));
        _db.SetSetting("test_key", "updated");
        Assert.Equal("updated", _db.GetSetting("test_key"));
    }

    [Fact]
    public void Persistence()
    {
        _db.AddSelectedApp(42);
        _db.MarkEvicted(42);
        _db.Dispose();

        var db2 = new Database(_dir);
        Assert.Contains(42u, db2.GetSelectedApps());
        Assert.Contains(42u, db2.GetEvictedApps());
        db2.Dispose();
    }

    [Fact]
    public void MarkPartial_SetsPartialStatus()
    {
        _db.AddSelectedApp(730);
        _db.MarkPartial(730);
        Assert.Equal("partial", _db.GetAppStatus(730));
        Assert.Contains(730u, _db.GetActiveApps()); // GetActiveApps includes partial
        Assert.DoesNotContain(730u, _db.GetEvictedApps());
    }

    [Fact]
    public void GetAppStatus_DefaultIsActive()
    {
        _db.AddSelectedApp(730);
        Assert.Equal("active", _db.GetAppStatus(730));
    }

    [Fact]
    public void GetAppStatus_NonExistent_ReturnsActive()
    {
        Assert.Equal("active", _db.GetAppStatus(999));
    }

    [Fact]
    public void SetAppStatus_AllStatuses()
    {
        _db.AddSelectedApp(730);
        foreach (var status in new[] { "active", "partial", "evicted", "pending" })
        {
            _db.SetAppStatus(730, status);
            Assert.Equal(status, _db.GetAppStatus(730));
        }
    }

    [Fact]
    public void GetAppsByStatus_FiltersCorrectly()
    {
        _db.AddSelectedApp(730);
        _db.AddSelectedApp(440);
        _db.AddSelectedApp(570);
        _db.MarkEvicted(730);
        _db.MarkPartial(440);
        // 570 remains active

        var evicted = _db.GetAppsByStatus("evicted");
        Assert.Single(evicted);
        Assert.Contains(730u, evicted);

        var partial = _db.GetAppsByStatus("partial");
        Assert.Single(partial);
        Assert.Contains(440u, partial);

        var activeAndPartial = _db.GetAppsByStatus("active", "partial");
        Assert.Equal(2, activeAndPartial.Count);
        Assert.Contains(440u, activeAndPartial);
        Assert.Contains(570u, activeAndPartial);
    }

    [Fact]
    public void GetActiveApps_IncludesActiveAndPartial()
    {
        _db.AddSelectedApp(730);
        _db.AddSelectedApp(440);
        _db.AddSelectedApp(570);
        _db.MarkPartial(440);
        _db.MarkEvicted(570);

        var active = _db.GetActiveApps();
        Assert.Equal(2, active.Count);
        Assert.Contains(730u, active);
        Assert.Contains(440u, active);
        Assert.DoesNotContain(570u, active);
    }

    [Fact]
    public void ClearDownloadedDepots_RemovesRecords()
    {
        var depots = new[] { new DepotState(100, "test", 999, 1, 1), new DepotState(101, "test2", 888, 1, 1) };
        _db.MarkDepotsDownloaded(depots);
        Assert.True(_db.IsAppUpToDate(depots));

        _db.ClearDownloadedDepots([100u]);
        Assert.False(_db.IsAppUpToDate(depots));

        // Depot 101 should still be present
        Assert.True(_db.IsAppUpToDate([new DepotState(101, "test2", 888, 1, 1)]));
    }

    [Fact]
    public void ClearDownloadedDepots_AllDepots_MakesNotUpToDate()
    {
        var depots = new[] { new DepotState(100, "test", 999, 1, 1) };
        _db.MarkDepotsDownloaded(depots);
        _db.ClearDownloadedDepots([100u]);
        Assert.False(_db.IsAppUpToDate(depots));
    }

    [Fact]
    public void ClearDownloadedDepots_NonExistentDepot_DoesNotThrow()
    {
        _db.ClearDownloadedDepots([999u]); // Should not throw
    }

    public void Dispose() { _db.Dispose(); try { Directory.Delete(_dir, true); } catch { } }
}
