using LancachePrefill.Data.Repositories;

namespace LancachePrefill.Api;

public static class CacheStatsEndpoints
{
    public static void MapCacheStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/cache-stats", (ISettingsRepository settingsRepo) =>
        {
            var cacheDir = Environment.GetEnvironmentVariable("LANCACHE_CACHE_DIR");
            long? diskTotal = null, diskFree = null;
            if (!string.IsNullOrEmpty(cacheDir) && Directory.Exists(cacheDir))
            {
                var drive = FindDriveFor(cacheDir);
                if (drive != null)
                {
                    diskTotal = drive.TotalSize;
                    diskFree = drive.AvailableFreeSpace;
                }
            }

            long? cacheBytes = long.TryParse(settingsRepo.GetSetting("stat_cache_bytes"), out var cb) ? cb : null;
            var scannedAt = settingsRepo.GetSetting("stat_cache_scanned_at");

            return Results.Ok(new
            {
                available = cacheBytes != null || diskTotal != null,
                cacheBytes,       // measured during the last scan walk
                scannedAt,        // ISO timestamp of that scan, null if never
                diskTotalBytes = diskTotal,
                diskFreeBytes = diskFree
            });
        });
    }

    /// <summary>
    /// Mount-aware drive lookup: on Linux, DirectoryInfo.Root is always "/" which
    /// reports the root filesystem, not the cache volume's mount. Pick the drive
    /// whose mount point is the longest prefix of the cache path instead.
    /// </summary>
    private static DriveInfo? FindDriveFor(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady && full.StartsWith(d.Name, StringComparison.Ordinal))
                .OrderByDescending(d => d.Name.Length)
                .FirstOrDefault();
        }
        catch { return null; }
    }
}
