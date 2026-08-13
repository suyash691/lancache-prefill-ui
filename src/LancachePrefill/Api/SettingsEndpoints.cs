using LancachePrefill.Data.Repositories;

namespace LancachePrefill.Api;

public static class SettingsEndpoints
{
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
                ["prefill_max_mbps"] = "0"
            };
            var saved = settingsRepo.GetAllSettings();
            foreach (var (k, v) in saved) defaults[k] = v;
            return Results.Ok(defaults);
        });

        group.MapPost("/", (Dictionary<string, string> settings, ISettingsRepository settingsRepo) =>
        {
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
            foreach (var (k, v) in settings) settingsRepo.SetSetting(k, v);
            return Results.Ok();
        });

        return group;
    }
}
