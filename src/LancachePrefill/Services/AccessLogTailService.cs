namespace LancachePrefill.Services;

/// <summary>
/// Tails the lancache nginx access.log (LANCACHE_LOGS_DIR mount) and feeds the
/// ActivityTracker. Starts at end-of-file (history is not replayed), survives
/// log rotation, and idles quietly when the mount is absent.
/// </summary>
public class AccessLogTailService : BackgroundService
{
    private readonly ActivityTracker _tracker;
    private readonly ILogger<AccessLogTailService> _log;

    public AccessLogTailService(ActivityTracker tracker, ILogger<AccessLogTailService> log)
    {
        _tracker = tracker;
        _log = log;
    }

    public static string? LogFilePath()
    {
        var dir = Environment.GetEnvironmentVariable("LANCACHE_LOGS_DIR");
        if (string.IsNullOrEmpty(dir)) return null;
        var path = Path.Combine(dir, "access.log");
        return File.Exists(path) ? path : null;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var announcedMissing = false;
        long position = -1; // -1 = start at end of file on first open

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var path = LogFilePath();
                if (path == null)
                {
                    if (!announcedMissing)
                    {
                        _log.LogInformation("No access.log (set LANCACHE_LOGS_DIR to enable the Activity view) — will keep checking");
                        announcedMissing = true;
                    }
                    await Task.Delay(TimeSpan.FromMinutes(5), ct);
                    continue;
                }
                announcedMissing = false;

                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                if (position < 0 || position > fs.Length) // first open, or rotated/truncated
                    position = position < 0 ? fs.Length : 0;
                fs.Seek(position, SeekOrigin.Begin);
                using var reader = new StreamReader(fs);

                while (!ct.IsCancellationRequested)
                {
                    string? line;
                    while ((line = await reader.ReadLineAsync(ct)) != null)
                    {
                        var entry = AccessLogParser.ParseLine(line);
                        if (entry != null) _tracker.Add(entry);
                    }
                    position = fs.Position;
                    await Task.Delay(2000, ct);

                    // Rotation check: the file shrank (copytruncate) or was replaced.
                    var current = new FileInfo(path);
                    if (!current.Exists || current.Length < position)
                        break; // reopen from 0 via outer loop
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Access log tail error — retrying in 30s");
                try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
