using LancachePrefill;
using LancachePrefill.Api;
using LancachePrefill.Data;
using LancachePrefill.Data.Repositories;
using LancachePrefill.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var configDir = Environment.GetEnvironmentVariable("CONFIG_DIR") ?? "/Config";
var port = Environment.GetEnvironmentVariable("PORT") ?? "28542";

// Preflight: the container runs as non-root (UID 1654). A config volume created
// by an older root-running container is not writable by it and every startup
// step (cert, DB, token) would fail with a bare UnauthorizedAccessException
// crash loop. Probe once and fail with instructions instead.
static void FailConfigDirUnusable(string dir, Exception ex)
{
    Console.Error.WriteLine(
        $"FATAL: config directory '{dir}' is not accessible by this user ({Environment.UserName}).\n" +
        $"       {ex.Message}\n" +
        "This container runs as non-root (UID 1654). If the volume was created by an\n" +
        "older (root) version, fix its ownership on the docker host:\n" +
        "    sudo chown -R 1654:1654 <host path mapped to /Config>\n" +
        "or temporarily run as root by uncommenting 'user: \"0:0\"' in docker-compose.yml.");
    Environment.Exit(64);
}

try
{
    Directory.CreateDirectory(configDir);
    var probe = Path.Combine(configDir, ".write-probe");
    File.WriteAllText(probe, "");
    File.Delete(probe);
}
catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
{
    FailConfigDirUnusable(configDir, ex);
}

System.Security.Cryptography.X509Certificates.X509Certificate2 cert;
try
{
    cert = CertificateManager.GetOrCreateCert(configDir);
}
catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
{
    // Dir is writable but an existing root-owned file (e.g. server.pfx 600) isn't.
    FailConfigDirUnusable(configDir, ex);
    throw; // unreachable — FailConfigDirUnusable exits
}
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
builder.Services.AddSingleton<IRunHistoryRepository>(sp => new RunHistoryRepository(sp.GetRequiredService<IDbContextFactory<PrefillDbContext>>()));

// Steam
builder.Services.AddSingleton(sp => new SteamSession(configDir, sp.GetRequiredService<ILogger<SteamSession>>()));
builder.Services.AddSingleton<ISteamSession>(sp => sp.GetRequiredService<SteamSession>());
builder.Services.AddSingleton<AppInfoProvider>();
builder.Services.AddSingleton<IAppInfoProvider>(sp => sp.GetRequiredService<AppInfoProvider>());
builder.Services.AddSingleton<IDepotDownloader, DepotDownloader>();
builder.Services.AddSingleton<DepotDownloader>(sp => (DepotDownloader)sp.GetRequiredService<IDepotDownloader>());

// Services
builder.Services.AddSingleton<JobCoordinator>();
builder.Services.AddSingleton<SseTicketStore>();
builder.Services.AddSingleton<ScanService>();
builder.Services.AddSingleton<PrefillService>();
builder.Services.AddSingleton<CacheBrowserService>();
builder.Services.AddSingleton<ActivityTracker>();
builder.Services.AddHostedService<AccessLogTailService>();
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
    // Endpoint routing matches paths case-insensitively, so this gate must too —
    // otherwise "/Api/apps" would skip the token check yet still reach the endpoint.
    // StartsWithSegments also enforces segment boundaries ("/api/authx" is not exempt).
    var path = ctx.Request.Path;
    if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWithSegments("/api/lancache", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWithSegments("/api/events", StringComparison.OrdinalIgnoreCase))
    {
        var token = ctx.Request.Headers["X-Session-Token"].FirstOrDefault();
        if (!SessionAuth.TokensMatch(session.SessionToken, token))
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
app.MapHistoryEndpoints();
app.MapCacheStatsEndpoints();
app.MapActivityEndpoints();
app.MapEventsEndpoint();

app.Lifetime.ApplicationStopping.Register(() =>
{
    app.Services.GetRequiredService<JobCoordinator>().CancelJob();
    app.Services.GetRequiredService<SteamSession>().Disconnect();
});

app.Run();

// Exposed so integration tests can host the app via WebApplicationFactory<Program>.
public partial class Program { }
