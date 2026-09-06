using LancachePrefill.Api;
using Xunit;

namespace LancachePrefill.Tests;

public class ThumbEndpointsTests
{
    [Fact]
    public void ParseCapsuleUrl_ExtractsCapsuleImage()
    {
        var json = """{"3357650":{"success":true,"data":{"name":"PRAGMATA","capsule_image":"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/3357650/abc123/capsule_231x87.jpg?t=1","header_image":"https://x/h.jpg"}}}""";
        Assert.StartsWith("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/3357650/",
            ThumbEndpoints.ParseCapsuleUrl(json, 3357650));
    }

    [Fact]
    public void ParseCapsuleUrl_FallsBackToHeaderImage()
    {
        var json = """{"42":{"success":true,"data":{"header_image":"https://cdn/x/header.jpg"}}}""";
        Assert.Equal("https://cdn/x/header.jpg", ThumbEndpoints.ParseCapsuleUrl(json, 42));
    }

    [Theory]
    [InlineData("""{"42":{"success":false}}""")]                                 // unknown app
    [InlineData("""{"42":{"success":true,"data":{}}}""")]                        // no images
    [InlineData("""{"42":{"success":true,"data":{"capsule_image":"http://insecure/x.jpg"}}}""")] // non-https rejected
    [InlineData("not json")]
    public void ParseCapsuleUrl_ReturnsNullOnBadInput(string json)
    {
        Assert.Null(ThumbEndpoints.ParseCapsuleUrl(json, 42));
    }
}
