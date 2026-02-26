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
                ["scan_concurrency"] = "4"
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
            foreach (var (k, v) in settings) settingsRepo.SetSetting(k, v);
            return Results.Ok();
        });

        return group;
    }
}
