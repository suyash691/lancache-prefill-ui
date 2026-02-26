using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LancachePrefill;

public static class CertificateManager
{
    public static X509Certificate2 GetOrCreateCert(string configDir)
    {
        var certPath = Path.Combine(configDir, "server.pfx");

        if (File.Exists(certPath))
        {
            try { return new X509Certificate2(certPath); }
            catch { }
        }

        var cert = GenerateSelfSigned();
        File.WriteAllBytes(certPath, cert.Export(X509ContentType.Pfx));

        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(certPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        return cert;
    }

    private static X509Certificate2 GenerateSelfSigned()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=lancache-prefill", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddDnsName(Environment.MachineName);
        san.AddIpAddress(System.Net.IPAddress.Loopback);
        san.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(san.Build());

        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(10));
        // Re-export needed on Linux for Kestrel compatibility
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
    }
}
