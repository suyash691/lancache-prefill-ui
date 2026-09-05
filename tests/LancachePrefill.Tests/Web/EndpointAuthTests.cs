using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LancachePrefill.Tests;

/// <summary>
/// Hosts the real app via WebApplicationFactory and verifies the auth middleware
/// and SSE token gate. Points CONFIG_DIR at a temp dir so cert/db init succeeds.
/// </summary>
public class LcpAppFactory : WebApplicationFactory<Program>, IDisposable
{
    public readonly string ConfigDir;
    private readonly string? _prevConfig;

    public LcpAppFactory()
    {
        ConfigDir = Path.Combine(Path.GetTempPath(), $"lancache-web-{Guid.NewGuid()}");
        Directory.CreateDirectory(ConfigDir);
        _prevConfig = Environment.GetEnvironmentVariable("CONFIG_DIR");
        // Read by Program.cs at host-build time (before CreateClient triggers build).
        Environment.SetEnvironmentVariable("CONFIG_DIR", ConfigDir);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Environment.SetEnvironmentVariable("CONFIG_DIR", _prevConfig);
        try { Directory.Delete(ConfigDir, true); } catch { }
    }
}

[Collection("EnvSerial")]
public class EndpointAuthTests : IClassFixture<LcpAppFactory>
{
    private readonly LcpAppFactory _factory;
    public EndpointAuthTests(LcpAppFactory factory) => _factory = factory;

    private HttpClient Client() => _factory.CreateClient();

    [Fact]
    public async Task Lancache_Endpoint_IsPublic()
    {
        var resp = await Client().GetAsync("/api/lancache");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("detected", body);
    }

    [Fact]
    public async Task AuthStatus_Endpoint_IsPublic_AndReportsLoggedOut()
    {
        var resp = await Client().GetAsync("/api/auth/status");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("\"loggedIn\":false", body);
    }

    [Fact]
    public async Task ProtectedEndpoint_NoToken_Returns401()
    {
        var resp = await Client().GetAsync("/api/apps");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // Endpoint routing is case-insensitive, so the auth gate must be too.
    // These would return 200 with the old case-sensitive StartsWith gate.
    [Theory]
    [InlineData("/Api/apps")]
    [InlineData("/API/settings")]
    [InlineData("/api/Apps")]
    [InlineData("/API/EVENTS")] // SSE gate is its own token check — must still 401
    public async Task ProtectedEndpoint_CaseVariantPath_NoToken_Returns401(string path)
    {
        var resp = await Client().GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WrongToken_Returns401()
    {
        var client = Client();
        client.DefaultRequestHeaders.Add("X-Session-Token", "not-the-real-token");
        var resp = await client.GetAsync("/api/apps");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Settings_RequiresAuth()
    {
        var resp = await Client().GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Sse_NoToken_Returns401()
    {
        var resp = await Client().GetAsync("/api/events");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Sse_WrongToken_Returns401()
    {
        var resp = await Client().GetAsync("/api/events?token=bogus");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
