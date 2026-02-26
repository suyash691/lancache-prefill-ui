using LancachePrefill;
using Xunit;

namespace LancachePrefill.Tests;

public class ModelTests
{
    [Fact]
    public void AppState_RecordEquality()
    {
        var d1 = new DepotState(1, "test", 100, 1, 1);
        var d2 = new DepotState(1, "test", 100, 1, 1);
        Assert.Equal(d1, d2);
    }

    [Fact]
    public void DownloadChunk_RecordEquality()
    {
        var c1 = new DownloadChunk(1, "ABC", 1024);
        var c2 = new DownloadChunk(1, "ABC", 1024);
        Assert.Equal(c1, c2);
    }

    [Fact]
    public void DepotState_DifferentManifest_NotEqual()
    {
        var d1 = new DepotState(1, "test", 100, 1, 1);
        var d2 = new DepotState(1, "test", 200, 1, 1);
        Assert.NotEqual(d1, d2);
    }
}
