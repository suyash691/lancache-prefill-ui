using LancachePrefill.Data.Repositories;
using LancachePrefill.Services;

namespace LancachePrefill.Api;

public static class SettingsEndpoints
{
    private static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
    {
        "prefill_schedule", "scan_schedule",
        "scan_concurrency", "prefill_concurrency", "prefill_max_mbps",
        "prefill_os_filter", "prefill_languages"
    };

    private static readonly HashSet<string> ValidOses = new(StringComparer.Ordinal)
        { "windows", "linux", "macos" };

    public static RouteGroupBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings");

        group.MapGet("/", (ISettingsRepository settingsRepo) =>
        {
            var defaults = new Dictionary<string, string>
            {
                ["prefill_schedule"] = Environment.GetEnvironmentVariable("PREFILL_SCHEDULE") ?? "0 4 * * *",
                ["scan_schedule"] = Environment.GetEnvironmentVariable("SCAN_SCHEDULE") ?? "0 3 */3 * *",
                ["scan_concurrency"] = "4",
                ["prefill_concurrency"] = "6",
                ["prefill_max_mbps"] = "0",
                ["prefill_os_filter"] = "windows",
                ["prefill_languages"] = ""
            };
            var saved = settingsRepo.GetAllSettings();
            foreach (var (k, v) in saved) defaults[k] = v;
            return Results.Ok(defaults);
        });

        group.MapPost("/", (Dictionary<string, string> settings, ISettingsRepository settingsRepo,
            AppInfoProvider? appInfoProvider, JobCoordinator jobs) =>
        {
            // Only known settings may be persisted — reject anything else instead of
            // letting callers stuff arbitrary rows into the settings table.
            var unknown = settings.Keys.Where(k => !AllowedKeys.Contains(k)).ToList();
            if (unknown.Count > 0)
                return Results.Json(new { error = $"Unknown setting(s): {string.Join(", ", unknown)}" }, statusCode: 400);

            // Validate the depot OS filter (csv of windows/linux/macos; empty = default windows)
            if (settings.TryGetValue("prefill_os_filter", out var osCsv) && !string.IsNullOrWhiteSpace(osCsv))
            {
                var bad = osCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(o => !ValidOses.Contains(o.ToLowerInvariant())).ToList();
                if (bad.Count > 0)
                    return Results.Json(new { error = $"prefill_os_filter must be a comma list of windows/linux/macos (got: {string.Join(", ", bad)})" }, statusCode: 400);
            }
            // Languages: free-form csv of Steam language codes (e.g. english, german)
            if (settings.TryGetValue("prefill_languages", out var langCsv)
                && !System.Text.RegularExpressions.Regex.IsMatch(langCsv, @"^[a-zA-Z_,\s\-]*$"))
                return Results.Json(new { error = "prefill_languages must be a comma list of language codes" }, statusCode: 400);

            // Validate cron expressions before saving
            foreach (var key in new[] { "prefill_schedule", "scan_schedule" })
            {
                if (settings.TryGetValue(key, out var cron) && !string.IsNullOrWhiteSpace(cron))
                {
                    try { Cronos.CronExpression.Parse(cron); }
                    catch { return Results.Json(new { error = $"Invalid cron expression for {key}: {cron}" }, statusCode: 400); }
                }
            }
            // Validate numeric throughput settings
            foreach (var key in new[] { "prefill_concurrency", "scan_concurrency" })
            {
                if (settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
                    && (!int.TryParse(v, out var n) || n < 1 || n > 30))
                    return Results.Json(new { error = $"{key} must be a number between 1 and 30" }, statusCode: 400);
            }
            if (settings.TryGetValue("prefill_max_mbps", out var mbps) && !string.IsNullOrWhiteSpace(mbps)
                && (!long.TryParse(mbps, out var m) || m < 0))
                return Results.Json(new { error = "prefill_max_mbps must be a non-negative number (0 = unlimited)" }, statusCode: 400);
            // Detect depot-filter changes before persisting: the appinfo cache holds
            // depot lists built under the OLD filters and must be rebuilt.
            var filtersChanged =
                (settings.TryGetValue("prefill_os_filter", out var newOs) && newOs != (settingsRepo.GetSetting("prefill_os_filter") ?? ""))
                || (settings.TryGetValue("prefill_languages", out var newLang) && newLang != (settingsRepo.GetSetting("prefill_languages") ?? ""));

            foreach (var (k, v) in settings) settingsRepo.SetSetting(k, v);

            if (filtersChanged)
            {
                appInfoProvider?.InvalidateCache();
                jobs.BumpVersion(); // nudge connected UIs to reload app lists
            }
            return Results.Ok();
        });

        return group;
    }
}
