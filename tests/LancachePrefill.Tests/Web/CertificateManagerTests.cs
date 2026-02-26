using LancachePrefill;
using Xunit;

namespace LancachePrefill.Tests;

public class CertificateManagerTests : IDisposable
{
    private readonly string _dir;

    public CertificateManagerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"lancache-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void GeneratesCert_OnFirstRun()
    {
        var cert = CertificateManager.GetOrCreateCert(_dir);
        Assert.NotNull(cert);
        Assert.True(cert.HasPrivateKey);
        Assert.Contains("lancache-prefill", cert.Subject);
    }

    [Fact]
    public void PersistsCert_ToFile()
    {
        CertificateManager.GetOrCreateCert(_dir);
        Assert.True(File.Exists(Path.Combine(_dir, "server.pfx")));
    }

    [Fact]
    public void ReloadsCert_OnSubsequentRun()
    {
        var cert1 = CertificateManager.GetOrCreateCert(_dir);
        var cert2 = CertificateManager.GetOrCreateCert(_dir);
        Assert.Equal(cert1.Thumbprint, cert2.Thumbprint);
    }

    [Fact]
    public void RegeneratesCert_IfCorrupt()
    {
        File.WriteAllText(Path.Combine(_dir, "server.pfx"), "garbage");
        var cert = CertificateManager.GetOrCreateCert(_dir);
        Assert.NotNull(cert);
        Assert.True(cert.HasPrivateKey);
    }

    [Fact]
    public void CertValidity_Is10Years()
    {
        var cert = CertificateManager.GetOrCreateCert(_dir);
        Assert.True(cert.NotAfter > DateTime.UtcNow.AddYears(9));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
