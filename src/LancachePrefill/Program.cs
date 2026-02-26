using LancachePrefill;
using LancachePrefill.Api;
using LancachePrefill.Data;
using LancachePrefill.Data.Repositories;
using LancachePrefill.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var configDir = Environment.GetEnvironmentVariable("CONFIG_DIR") ?? "/Config";
var port = Environment.GetEnvironmentVariable("PORT") ?? "28542";

var cert = CertificateManager.GetOrCreateCert(configDir);
builder.WebHost.ConfigureKestrel(k => k.ListenAnyIP(int.Parse(port), o => o.UseHttps(cert)));

// Localization
builder.Services.AddLocalization();

// Data layer
var dbPath = Path.Combine(configDir, "lancache-prefill.db");
builder.Services.AddDbContextFactory<PrefillDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath}"));

// Suppress verbose EF Core SQL logging in production
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Services.AddSingleton<IAppRepository>(sp => new AppRepository(sp.GetRequiredService<IDbContextFactory<PrefillDbContext>>()));
builder.Services.AddSingleton<ICacheRepository>(sp => new CacheRepository(sp.GetRequiredService<IDbContextFactory<PrefillDbContext>>()));
builder.Services.AddSingleton<IScanRepository>(sp => new ScanRepository(sp.GetRequiredService<IDbContextFactory<PrefillDbContext>>()));
builder.Services.AddSingleton<ISettingsRepository>(sp => new SettingsRepository(sp.GetRequiredService<IDbContextFactory<PrefillDbContext>>()));

// Steam
builder.Services.AddSingleton(sp => new SteamSession(configDir, sp.GetRequiredService<ILogger<SteamSession>>()));
builder.Services.AddSingleton<ISteamSession>(sp => sp.GetRequiredService<SteamSession>());
builder.Services.AddSingleton<AppInfoProvider>();
builder.Services.AddSingleton<IAppInfoProvider>(sp => sp.GetRequiredService<AppInfoProvider>());
builder.Services.AddSingleton<IDepotDownloader, DepotDownloader>();
builder.Services.AddSingleton<DepotDownloader>(sp => (DepotDownloader)sp.GetRequiredService<IDepotDownloader>());

// Services
builder.Services.AddSingleton<JobCoordinator>();
builder.Services.AddSingleton<ScanService>();
builder.Services.AddSingleton<PrefillService>();
builder.Services.AddSingleton<CacheBrowserService>();
builder.Services.AddHostedService<PrefillScheduler>();
builder.Services.AddHostedService<ScanScheduler>();

var app = builder.Build();

// Request localization — allows culture selection via Accept-Language, query string, or cookie
var supportedCultures = new[] { "en" };
app.UseRequestLocalization(opt =>
{
    opt.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("en");
    opt.AddSupportedCultures(supportedCultures);
    opt.AddSupportedUICultures(supportedCultures);
});

app.UseDefaultFiles();
app.UseStaticFiles();

// Initialize DB via EF Core migrations
using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<PrefillDbContext>();
    ctx.Database.Migrate();
    ctx.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL");
}

// Initialize services
var scanService = app.Services.GetRequiredService<ScanService>();
var cacheBrowser = app.Services.GetRequiredService<CacheBrowserService>();
scanService.SetCacheBrowser(cacheBrowser);
scanService.RestoreFromDb();

// Auth middleware — skip for auth endpoints, lancache check, and SSE
var session = app.Services.GetRequiredService<SteamSession>();
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";
    if (path.StartsWith("/api/") && !path.StartsWith("/api/auth/") && !path.StartsWith("/api/lancache") && !path.StartsWith("/api/events"))
    {
        var token = ctx.Request.Headers["X-Session-Token"].FirstOrDefault();
        if (session.SessionToken == null || token != session.SessionToken)
        {
            ctx.Response.StatusCode = 401;
            return;
        }
    }
    await next();
});

// Lancache detection (no auth required)
app.MapGet("/api/lancache", async (DepotDownloader? downloader) =>
{
    var ip = downloader != null ? await downloader.DetectLancacheAsync() : null;
    return ip != null
        ? Results.Ok(new { detected = true, ip })
        : Results.Ok(new { detected = false, ip = (string?)null });
});

app.MapPost("/api/cancel", (JobCoordinator jobs) => { jobs.CancelJob(); return Results.Ok(); });

// Route groups
app.MapAuthEndpoints();
app.MapAppEndpoints();
app.MapLibraryEndpoints();
app.MapScanEndpoints();
app.MapPrefillEndpoints();
app.MapEvictedEndpoints();
app.MapCacheBrowserEndpoints();
app.MapSettingsEndpoints();
app.MapEventsEndpoint();

app.Lifetime.ApplicationStopping.Register(() =>
{
    app.Services.GetRequiredService<JobCoordinator>().CancelJob();
    app.Services.GetRequiredService<SteamSession>().Disconnect();
});

app.Run();
