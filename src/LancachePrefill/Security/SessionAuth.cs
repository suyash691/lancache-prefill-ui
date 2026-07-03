using System.Security.Cryptography;
using System.Text;

namespace LancachePrefill;

/// <summary>Session-token comparison helpers.</summary>
public static class SessionAuth
{
    /// <summary>
    /// Constant-time comparison of the expected session token against a
    /// client-supplied value. Returns false if either is null/empty.
    /// Avoids leaking token contents via response-timing side channels.
    /// </summary>
    public static bool TokensMatch(string? expected, string? provided)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided))
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(provided));
    }
}
