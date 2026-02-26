using System.Net;

namespace LancachePrefill;

public static class NetworkUtils
{
    public static bool IsPrivateIp(IPAddress addr)
    {
        var b = addr.GetAddressBytes();
        if (b.Length != 4) return false;
        return b[0] == 10 || b[0] == 127 ||
            (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
            (b[0] == 192 && b[1] == 168);
    }
}
