using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace LancachePrefill;

/// <summary>
/// Single-use, short-lived tickets for authenticating the SSE stream.
/// EventSource cannot send headers, and putting the long-lived session token
/// in the query string leaks it into logs and proxies — so the frontend
/// exchanges the session token (via a normal header-authed POST) for a
/// one-shot ticket immediately before connecting.
/// </summary>
public sealed class SseTicketStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, DateTime> _tickets = new();

    public string Issue()
    {
        // Opportunistic cleanup — the dictionary only ever holds a handful of entries.
        foreach (var (ticket, expiry) in _tickets)
            if (expiry < DateTime.UtcNow)
                _tickets.TryRemove(ticket, out _);

        var t = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tickets[t] = DateTime.UtcNow + Ttl;
        return t;
    }

    /// <summary>Atomically consumes a ticket. Valid at most once, within its TTL.</summary>
    public bool Redeem(string? ticket)
    {
        if (string.IsNullOrEmpty(ticket)) return false;
        return _tickets.TryRemove(ticket, out var expiry) && expiry >= DateTime.UtcNow;
    }
}
