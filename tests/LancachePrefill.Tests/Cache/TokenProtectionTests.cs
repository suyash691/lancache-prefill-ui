using LancachePrefill;
using Xunit;

namespace LancachePrefill.Tests;

public class TokenProtectionTests : IDisposable
{
    private readonly string _dir;

    public TokenProtectionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"lancache-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void RoundTrip()
    {
        var original = "some-refresh-token-value-12345";
        var encrypted = TokenProtection.Encrypt(original, _dir);
        var decrypted = TokenProtection.Decrypt(encrypted, _dir);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void EncryptedDiffersFromPlaintext()
    {
        var original = "my-token";
        var encrypted = TokenProtection.Encrypt(original, _dir);
        Assert.NotEqual(original, encrypted);
    }

    [Fact]
    public void DifferentConfigDir_CannotDecrypt()
    {
        var dir2 = Path.Combine(Path.GetTempPath(), $"lancache-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir2);

        var encrypted = TokenProtection.Encrypt("secret", _dir);
        var result = TokenProtection.Decrypt(encrypted, dir2);
        Assert.Null(result);

        Directory.Delete(dir2, true);
    }

    [Fact]
    public void TamperedData_ReturnsNull()
    {
        var encrypted = TokenProtection.Encrypt("secret", _dir);
        var bytes = Convert.FromBase64String(encrypted);
        bytes[^1] ^= 0xFF; // flip last byte
        var tampered = Convert.ToBase64String(bytes);

        Assert.Null(TokenProtection.Decrypt(tampered, _dir));
    }

    [Fact]
    public void TwoEncryptions_ProduceDifferentCiphertext()
    {
        var a = TokenProtection.Encrypt("same", _dir);
        var b = TokenProtection.Encrypt("same", _dir);
        Assert.NotEqual(a, b); // different nonces
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
