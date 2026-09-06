using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LancachePrefill.Services;

public record AccessLogEntry(string Service, string ClientIp, DateTime Time,
    int Status, long Bytes, string CacheStatus, string Host);

/// <summary>Parses lancachenet/monolithic 'cachelog' formatted access.log lines.</summary>
public static partial class AccessLogParser
{
    // [steam] 10.0.0.5 / - - - [05/Sep/2026:20:00:00 +0000] "GET /depot/... HTTP/1.1"
    //   200 1048576 "-" "Valve/Steam HTTP Client 1.0" "HIT" "lancache.steamcontent.com" "bytes=0-1048575"
    private static readonly Regex LineRx = new(
        @"^\[(?<svc>[^\]]*)\]\s+(?<ip>\S+)\s+\S+\s+\S+\s+\S+\s+\S+\s+\[(?<time>[^\]]+)\]\s+""[^""]*""\s+(?<status>\d{3})\s+(?<bytes>\d+)\s+""[^""]*""\s+""[^""]*""\s+""(?<cache>[^""]*)""\s+""(?<host>[^""]*)""",
        RegexOptions.Compiled);

    public static AccessLogEntry? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var m = LineRx.Match(line);
        if (!m.Success) return null;
        if (!DateTime.TryParseExact(m.Groups["time"].Value, "dd/MMM/yyyy:HH:mm:ss zzz",
                CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var time))
            return null;
        return new AccessLogEntry(
            m.Groups["svc"].Value,
            m.Groups["ip"].Value,
            time,
            int.Parse(m.Groups["status"].Value),
            long.Parse(m.Groups["bytes"].Value),
            m.Groups["cache"].Value.ToUpperInvariant(),
            m.Groups["host"].Value);
    }
}

/// <summary>
/// In-memory rolling activity stats fed by the access-log tail. 24h of 5-minute
/// hit/miss buckets plus per-client totals (since startup; a LAN has few clients).
/// </summary>
public class ActivityTracker
{
    public record Bucket(DateTime Start)
    {
        public long HitBytes;
        public long MissBytes;
        public int Hits;
        public int Misses;
    }

    public class ClientStat
    {
        public long Bytes;
        public int Hits;
        public int Misses;
        public DateTime LastSeen;
    }

    private static readonly TimeSpan BucketSize = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<DateTime, Bucket> _buckets = new();
    private readonly ConcurrentDictionary<string, ClientStat> _clients = new();

    /// <summary>When tracking began (tail starts at end-of-log, so stats cover container lifetime only).</summary>
    public DateTime StartedAt { get; } = DateTime.UtcNow;

    public void Add(AccessLogEntry e)
    {
        // REVALIDATED = served from local cache after a conditional upstream check —
        // the content bytes came from the cache, so it counts as a hit.
        var isHit = e.CacheStatus is "HIT" or "REVALIDATED";
        var isMiss = e.CacheStatus is "MISS" or "EXPIRED" or "UPDATING";
        if (!isHit && !isMiss) return; // '-', BYPASS etc. carry no cache signal

        var key = new DateTime(e.Time.Ticks - e.Time.Ticks % BucketSize.Ticks, DateTimeKind.Utc);
        var b = _buckets.GetOrAdd(key, k => new Bucket(k));
        var c = _clients.GetOrAdd(e.ClientIp, _ => new ClientStat());
        lock (b)
        {
            if (isHit) { b.HitBytes += e.Bytes; b.Hits++; }
            else { b.MissBytes += e.Bytes; b.Misses++; }
        }
        lock (c)
        {
            c.Bytes += e.Bytes;
            if (isHit) c.Hits++; else c.Misses++;
            c.LastSeen = e.Time;
        }
        Prune();
    }

    private void Prune()
    {
        var cutoff = DateTime.UtcNow - Retention;
        foreach (var key in _buckets.Keys)
            if (key < cutoff) _buckets.TryRemove(key, out _);
    }

    public record BucketSnapshot(DateTime Start, long HitBytes, long MissBytes, int Hits, int Misses);
    public record ClientSnapshot(string Ip, long Bytes, int Hits, int Misses, DateTime LastSeen);
    public record ActivitySnapshot(long HitBytes, long MissBytes, int Hits, int Misses,
        double? HitRatio, List<BucketSnapshot> Buckets, List<ClientSnapshot> Clients);

    public ActivitySnapshot Snapshot()
    {
        var buckets = _buckets.Values.OrderBy(b => b.Start)
            .Select(b => new BucketSnapshot(b.Start, b.HitBytes, b.MissBytes, b.Hits, b.Misses))
            .ToList();
        long hitBytes = buckets.Sum(b => b.HitBytes), missBytes = buckets.Sum(b => b.MissBytes);
        int hits = buckets.Sum(b => b.Hits), misses = buckets.Sum(b => b.Misses);
        return new ActivitySnapshot(
            hitBytes, missBytes, hits, misses,
            hits + misses > 0 ? (double)hits / (hits + misses) : null,
            buckets,
            _clients
                .Select(kv => new ClientSnapshot(kv.Key, kv.Value.Bytes, kv.Value.Hits, kv.Value.Misses, kv.Value.LastSeen))
                .OrderByDescending(c => c.Bytes).Take(20).ToList());
    }
}
