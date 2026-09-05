using LancachePrefill.Services;
using Xunit;

namespace LancachePrefill.Tests;

public class AccessLogTests
{
    private const string HitLine =
        "[steam] 10.0.0.5 / - - - [05/Sep/2026:20:00:00 +0000] \"GET /depot/731/chunk/abc HTTP/1.1\" 200 1048576 \"-\" \"Valve/Steam HTTP Client 1.0\" \"HIT\" \"lancache.steamcontent.com\" \"bytes=0-1048575\"";
    private const string MissLine =
        "[epic] 10.0.0.7 / - - - [05/Sep/2026:20:01:00 +0000] \"GET /Builds/Org/x.manifest HTTP/1.1\" 200 52428800 \"-\" \"EpicGamesLauncher\" \"MISS\" \"download.epicgames.com\" \"-\"";

    [Fact]
    public void ParsesHitLine()
    {
        var e = AccessLogParser.ParseLine(HitLine);
        Assert.NotNull(e);
        Assert.Equal("steam", e.Service);
        Assert.Equal("10.0.0.5", e.ClientIp);
        Assert.Equal(200, e.Status);
        Assert.Equal(1048576, e.Bytes);
        Assert.Equal("HIT", e.CacheStatus);
        Assert.Equal("lancache.steamcontent.com", e.Host);
        Assert.Equal(new DateTime(2026, 9, 5, 20, 0, 0, DateTimeKind.Utc), e.Time);
    }

    [Fact]
    public void ParsesMissLine_OtherService()
    {
        var e = AccessLogParser.ParseLine(MissLine);
        Assert.NotNull(e);
        Assert.Equal("epic", e.Service);
        Assert.Equal("MISS", e.CacheStatus);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage line without structure")]
    [InlineData("127.0.0.1 - - [05/Sep/2026:20:00:00 +0000] \"GET / HTTP/1.1\" 200 0")] // plain combined format
    public void UnparseableLines_ReturnNull(string line)
    {
        Assert.Null(AccessLogParser.ParseLine(line));
    }

    [Fact]
    public void Tracker_AccumulatesHitsAndMisses()
    {
        var tracker = new ActivityTracker();
        var now = DateTime.UtcNow;
        tracker.Add(new AccessLogEntry("steam", "10.0.0.5", now, 200, 100, "HIT", "h"));
        tracker.Add(new AccessLogEntry("steam", "10.0.0.5", now, 200, 300, "MISS", "h"));
        tracker.Add(new AccessLogEntry("steam", "10.0.0.6", now, 200, 50, "HIT", "h"));
        tracker.Add(new AccessLogEntry("steam", "10.0.0.6", now, 200, 10, "-", "h")); // no cache signal → ignored

        var s = tracker.Snapshot();
        Assert.Equal(150L, s.HitBytes);
        Assert.Equal(300L, s.MissBytes);
        Assert.Equal(2, s.Hits);
        Assert.Equal(1, s.Misses);
        Assert.Equal(2, s.Clients.Count); // 10.0.0.6's '-' line ignored, but its HIT counted
    }
}
