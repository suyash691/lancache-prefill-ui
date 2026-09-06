using LancachePrefill;
using Xunit;

namespace LancachePrefill.Tests;

/// <summary>
/// Tests for the optional TOKEN_ENCRYPTION_KEY user secret. In the EnvSerial
/// collection because they mutate process-wide environment variables.
/// </summary>
[Collection("EnvSerial")]
public class TokenProtectionEnvKeyTests : IDisposable
{
    private readonly string _dir;
    private readonly string? _prevKey;

    public TokenProtectionEnvKeyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"lancache-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
        _prevKey = Environment.GetEnvironmentVariable(TokenProtection.KeyEnvVar);
        Environment.SetEnvironmentVariable(TokenProtection.KeyEnvVar, null);
    }

    [Fact]
    public void UserKey_RoundTrip()
    {
        Environment.SetEnvironmentVariable(TokenProtection.KeyEnvVar, "a-long-random-user-secret");
        var encrypted = TokenProtection.Encrypt("refresh-token", _dir);
        Assert.Equal("refresh-token", TokenProtection.Decrypt(encrypted, _dir));
    }

    [Fact]
    public void LegacyCiphertext_StillDecrypts_AfterUserKeyIntroduced()
    {
        // Encrypted before the user set TOKEN_ENCRYPTION_KEY...
        var legacy = TokenProtection.Encrypt("refresh-token", _dir);
        // ...must still decrypt afterwards (fallback key), enabling seamless migration.
        Environment.SetEnvironmentVariable(TokenProtection.KeyEnvVar, "a-long-random-user-secret");
        Assert.Equal("refresh-token", TokenProtection.Decrypt(legacy, _dir));
    }

    [Fact]
    public void UserKeyCiphertext_DoesNotDecrypt_WithoutTheKey()
    {
        Environment.SetEnvironmentVariable(TokenProtection.KeyEnvVar, "a-long-random-user-secret");
        var encrypted = TokenProtection.Encrypt("refresh-token", _dir);
        Environment.SetEnvironmentVariable(TokenProtection.KeyEnvVar, null);
        Assert.Null(TokenProtection.Decrypt(encrypted, _dir));
    }

    [Fact]
    public void UserKeyCiphertext_DoesNotDecrypt_WithWrongKey()
    {
        Environment.SetEnvironmentVariable(TokenProtection.KeyEnvVar, "correct-secret");
        var encrypted = TokenProtection.Encrypt("refresh-token", _dir);
        Environment.SetEnvironmentVariable(TokenProtection.KeyEnvVar, "wrong-secret");
        Assert.Null(TokenProtection.Decrypt(encrypted, _dir));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(TokenProtection.KeyEnvVar, _prevKey);
        try { Directory.Delete(_dir, true); } catch { }
    }
}
