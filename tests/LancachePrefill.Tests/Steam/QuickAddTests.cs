using System.Collections.Concurrent;
using LancachePrefill;
using LancachePrefill.Api;
using Xunit;

namespace LancachePrefill.Tests;

public class QuickAddTests
{
    [Fact]
    public void ParseTopAppIds_ReadsRanksInOrder_AndCaps()
    {
        const string json = """
            {"response":{"ranks":[
                {"rank":1,"appid":730,"concurrent_in_game":900000},
                {"rank":2,"appid":570,"concurrent_in_game":500000},
                {"rank":3,"appid":578080,"concurrent_in_game":300000}
            ]}}
            """;
        Assert.Equal(new List<uint> { 730, 570 }, LibraryEndpoints.ParseTopAppIds(json, 2));
        Assert.Equal(3, LibraryEndpoints.ParseTopAppIds(json, 50).Count);
    }

    [Fact]
    public void ParseTopAppIds_EmptyOrMalformed_ReturnsEmpty()
    {
        Assert.Empty(LibraryEndpoints.ParseTopAppIds("{}", 10));
        Assert.Empty(LibraryEndpoints.ParseTopAppIds("""{"response":{}}""", 10));
    }

    [Fact]
    public void ComputeRecentAppIds_FiltersByCutoff_AndDedupes()
    {
        var packageApps = new ConcurrentDictionary<uint, List<uint>>();
        packageApps[100] = new List<uint> { 730, 440 };
        packageApps[200] = new List<uint> { 440 };   // overlaps with pkg 100
        packageApps[300] = new List<uint> { 570 };   // too old

        var now = DateTime.UtcNow;
        var licenses = new List<(uint, DateTime)>
        {
            (100u, now.AddDays(-1)),
            (200u, now.AddDays(-5)),
            (300u, now.AddDays(-30)),  // outside the 14-day window
            (999u, now),               // no package-apps mapping — ignored
        };

        var ids = SteamSession.ComputeRecentAppIds(licenses, packageApps, now.AddDays(-14));
        Assert.Equal(2, ids.Count);        // 440 deduped across pkgs 100/200
        Assert.Contains(730u, ids);
        Assert.Contains(440u, ids);
        Assert.DoesNotContain(570u, ids);  // pkg 300 is too old
    }
}
