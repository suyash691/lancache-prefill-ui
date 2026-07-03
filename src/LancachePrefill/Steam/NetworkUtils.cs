using System.Net;
using System.Net.Sockets;

namespace LancachePrefill;

public static class NetworkUtils
{
    public static bool IsPrivateIp(IPAddress addr)
    {
        if (addr.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IsLoopback(addr)) return true;   // ::1
            if (addr.IsIPv6LinkLocal) return true;         // fe80::/10
            var v6 = addr.GetAddressBytes();
            if (v6.Length == 16 && (v6[0] & 0xFE) == 0xFC) return true; // fc00::/7 (ULA)
            return false;
        }

        var b = addr.GetAddressBytes();
        if (b.Length != 4) return false;
        return b[0] == 10 || b[0] == 127 ||
            (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
            (b[0] == 192 && b[1] == 168);
    }
}
