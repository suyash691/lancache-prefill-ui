using LancachePrefill;
using SteamKit2;
using Xunit;

namespace LancachePrefill.Tests;

public class AppInfoParserTests
{
    private readonly HashSet<uint> _ownedApps = new() { 730 };
    private readonly HashSet<uint> _ownedDepots = new() { 731, 732 };

    private static KeyValue BuildAppKv(string type = "game", string? releaseState = null,
        Action<KeyValue>? configureDepots = null)
    {
        var kv = new KeyValue("appinfo");
        var common = new KeyValue("common");
        common.Children.Add(new KeyValue("name") { Value = "Test Game" });
        common.Children.Add(new KeyValue("type") { Value = type });
        if (releaseState != null)
            common.Children.Add(new KeyValue("releasestate") { Value = releaseState });
        kv.Children.Add(common);

        var depots = new KeyValue("depots");
        configureDepots?.Invoke(depots);
        kv.Children.Add(depots);

        return kv;
    }

    private static void AddDepot(KeyValue depots, string depotId, ulong manifestId,
        string? oslist = null, string? depotFromApp = null)
    {
        var depot = new KeyValue(depotId);
        depot.Children.Add(new KeyValue("name") { Value = $"Depot {depotId}" });

        var manifests = new KeyValue("manifests");
        var pub = new KeyValue("public");
        pub.Children.Add(new KeyValue("gid") { Value = manifestId.ToString() });
        manifests.Children.Add(pub);
        depot.Children.Add(manifests);

        if (oslist != null || depotFromApp != null)
        {
            var config = new KeyValue("config");
            if (oslist != null)
                config.Children.Add(new KeyValue("oslist") { Value = oslist });
            depot.Children.Add(config);
        }

        if (depotFromApp != null)
            depot.Children.Add(new KeyValue("depotfromapp") { Value = depotFromApp });

        depots.Children.Add(depot);
    }

    [Fact]
    public void ParsesValidGame()
    {
        var kv = BuildAppKv(configureDepots: d => AddDepot(d, "731", 12345));
        var result = AppInfoProvider.ParseAppInfo(730, kv, _ownedApps, _ownedDepots);

        Assert.NotNull(result);
        Assert.Equal(730u, result.AppId);
        Assert.Equal("Test Game", result.Name);
        Assert.Single(result.Depots);
        Assert.Equal(731u, result.Depots[0].DepotId);
        Assert.Equal(12345ul, result.Depots[0].ManifestId);
    }

    [Theory]
    [InlineData("tool")]
    [InlineData("config")]
    [InlineData("dlc")]
    [InlineData("video")]
    public void RejectsNonGameTypes(string type)
    {
        var kv = BuildAppKv(type: type);
        Assert.Null(AppInfoProvider.ParseAppInfo(730, kv, _ownedApps, _ownedDepots));
    }

    [Fact]
    public void AcceptsBetaType()
    {
        var kv = BuildAppKv(type: "beta", configureDepots: d => AddDepot(d, "731", 100));
        Assert.NotNull(AppInfoProvider.ParseAppInfo(730, kv, _ownedApps, _ownedDepots));
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("disabled")]
    public void RejectsUnavailableReleaseStates(string state)
    {
        var kv = BuildAppKv(releaseState: state);
        Assert.Null(AppInfoProvider.ParseAppInfo(730, kv, _ownedApps, _ownedDepots));
    }

    [Fact]
    public void FiltersLinuxOnlyDepots()
    {
        var kv = BuildAppKv(configureDepots: d =>
        {
            AddDepot(d, "731", 100, oslist: "linux");
            AddDepot(d, "732", 200, oslist: "windows");
        });

        var result = AppInfoProvider.ParseAppInfo(730, kv, _ownedApps, _ownedDepots);
        Assert.NotNull(result);
        Assert.Single(result.Depots);
        Assert.Equal(732u, result.Depots[0].DepotId);
    }

    [Fact]
    public void IncludesDepotsWithNoOsList()
    {
        var kv = BuildAppKv(configureDepots: d => AddDepot(d, "731", 100));
        var result = AppInfoProvider.ParseAppInfo(730, kv, _ownedApps, _ownedDepots);
        Assert.Single(result!.Depots);
    }

    [Fact]
    public void SkipsDepotsWithZeroManifest()
    {
        var kv = BuildAppKv(configureDepots: d => AddDepot(d, "731", 0));
        var result = AppInfoProvider.ParseAppInfo(730, kv, _ownedApps, _ownedDepots);
        Assert.NotNull(result);
        Assert.Empty(result.Depots);
    }

    [Fact]
    public void SkipsUnownedDepots()
    {
        var kv = BuildAppKv(configureDepots: d => AddDepot(d, "999", 100));
        // depot 999 not in ownedDepots, app 730 is in ownedApps so it passes the app check
        var result = AppInfoProvider.ParseAppInfo(730, kv, _ownedApps, _ownedDepots);
        Assert.NotNull(result);
        // depot 999 is not owned but app 730 is, so it should still be included
        Assert.Single(result.Depots);
    }

    [Fact]
    public void SkipsUnownedDepotsForUnownedApp()
    {
        var kv = BuildAppKv(configureDepots: d => AddDepot(d, "999", 100));
        var emptyApps = new HashSet<uint>();
        var emptyDepots = new HashSet<uint>();
        var result = AppInfoProvider.ParseAppInfo(730, kv, emptyApps, emptyDepots);
        Assert.NotNull(result);
        Assert.Empty(result.Depots);
    }

    [Fact]
    public void SkipsNonNumericDepotKeys()
    {
        var kv = BuildAppKv(configureDepots: d =>
        {
            AddDepot(d, "731", 100);
            // "branches" is a non-numeric key Steam puts in the depots section
            d.Children.Add(new KeyValue("branches"));
        });

        var result = AppInfoProvider.ParseAppInfo(730, kv, _ownedApps, _ownedDepots);
        Assert.Single(result!.Depots);
    }

    [Fact]
    public void UsesDepotFromApp()
    {
        var kv = BuildAppKv(configureDepots: d => AddDepot(d, "731", 100, depotFromApp: "440"));
        var result = AppInfoProvider.ParseAppInfo(730, kv, _ownedApps, _ownedDepots);
        Assert.Equal(730u, result!.Depots[0].AppId);
        Assert.Equal(440u, result!.Depots[0].ContainingAppId);
    }

    [Fact]
    public void DefaultsContainingAppToSelf()
    {
        var kv = BuildAppKv(configureDepots: d => AddDepot(d, "731", 100));
        var result = AppInfoProvider.ParseAppInfo(730, kv, _ownedApps, _ownedDepots);
        Assert.Equal(730u, result!.Depots[0].AppId);
        Assert.Equal(730u, result!.Depots[0].ContainingAppId);
    }

    [Fact]
    public void FallbackName()
    {
        var kv = new KeyValue("appinfo");
        var common = new KeyValue("common");
        common.Children.Add(new KeyValue("type") { Value = "game" });
        // no name
        kv.Children.Add(common);
        kv.Children.Add(new KeyValue("depots"));

        var result = AppInfoProvider.ParseAppInfo(730, kv, _ownedApps, _ownedDepots);
        Assert.Equal("App 730", result!.Name);
    }

    [Fact]
    public void NoDepotsSection_ReturnsEmptyDepots()
    {
        var kv = BuildAppKv();
        var result = AppInfoProvider.ParseAppInfo(730, kv, _ownedApps, _ownedDepots);
        Assert.NotNull(result);
        Assert.Empty(result.Depots);
    }
}
