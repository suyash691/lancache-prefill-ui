using System.Collections.Concurrent;

namespace LancachePrefill.Api;

/// <summary>
/// Thumbnail resolver for titles whose capsule only exists at a content-hashed
/// store URL (everything released since ~2024). The frontend tries the legacy
/// CDN path first and only falls back here, so this endpoint sees a handful of
/// apps per user. Unauthenticated by necessity — &lt;img&gt; tags cannot send the
/// session-token header — but it only 302s to public Steam CDN URLs returned by
/// Steam's own store API, caches results, and throttles store lookups.
/// </summary>
public static class ThumbEndpoints
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly ConcurrentDictionary<uint, (string? Url, DateTime At)> _cache = new();
    // At most 2 concurrent store-API lookups; excess requests 404 (image hides)
    // rather than queueing — protects Steam's rate limits and stops an
    // unauthenticated caller using us as a store-API amplifier.
    private static readonly SemaphoreSlim _storeThrottle = new(2, 2);
    private static readonly TimeSpan PositiveTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromMinutes(30);

    /// <summary>Extracts the capsule (or header) image URL from a store appdetails response.</summary>
    public static string? ParseCapsuleUrl(string json, uint appId)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(appId.ToString(), out var entry)
                || !entry.TryGetProperty("success", out var ok) || !ok.GetBoolean()
                || !entry.TryGetProperty("data", out var data))
                return null;
            foreach (var field in new[] { "capsule_image", "header_image" })
                if (data.TryGetProperty(field, out var v) && v.GetString() is { Length: > 0 } url
                    && url.StartsWith("https://"))
                    return url;
            return null;
        }
        catch { return null; }
    }

    public static void MapThumbEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/thumb/{appId}", async (uint appId, ILoggerFactory lf) =>
        {
            if (_cache.TryGetValue(appId, out var hit)
                && DateTime.UtcNow - hit.At < (hit.Url != null ? PositiveTtl : NegativeTtl))
                return hit.Url != null ? Results.Redirect(hit.Url) : Results.NotFound();

            if (!await _storeThrottle.WaitAsync(TimeSpan.FromSeconds(3)))
                return Results.NotFound(); // busy — the img just stays hidden this render

            try
            {
                var json = await _http.GetStringAsync(
                    $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic");
                var url = ParseCapsuleUrl(json, appId);
                _cache[appId] = (url, DateTime.UtcNow);
                return url != null ? Results.Redirect(url) : Results.NotFound();
            }
            catch (Exception ex)
            {
                lf.CreateLogger("Thumb").LogDebug(ex, "Thumb lookup failed for {AppId}", appId);
                _cache[appId] = (null, DateTime.UtcNow);
                return Results.NotFound();
            }
            finally { _storeThrottle.Release(); }
        });
    }
}
