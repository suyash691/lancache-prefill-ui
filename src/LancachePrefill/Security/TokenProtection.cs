using System.Security.Cryptography;
using System.Text;

namespace LancachePrefill;

public static class TokenProtection
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>
    /// Environment variable holding an optional user-supplied encryption secret.
    /// When set, the token key derives from this value instead of the on-disk
    /// machine identity — protecting the stored Steam refresh token from anyone
    /// who obtains a copy of the config volume.
    /// </summary>
    public const string KeyEnvVar = "TOKEN_ENCRYPTION_KEY";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _keyCache = new();

    public static string Encrypt(string plaintext, string configDir)
    {
        var key = GetEncryptKey(configDir);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var combined = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(combined, 0);
        tag.CopyTo(combined, NonceSize);
        ciphertext.CopyTo(combined, NonceSize + TagSize);
        return Convert.ToBase64String(combined);
    }

    public static string? Decrypt(string encoded, string configDir)
    {
        byte[] combined;
        try { combined = Convert.FromBase64String(encoded); }
        catch (FormatException) { return null; }
        if (combined.Length < NonceSize + TagSize) return null;

        var nonce = combined[..NonceSize];
        var tag = combined[NonceSize..(NonceSize + TagSize)];
        var ciphertext = combined[(NonceSize + TagSize)..];

        // Try the user-supplied key first (when configured), then the legacy
        // machine-identity key so tokens encrypted before TOKEN_ENCRYPTION_KEY
        // was set still decrypt; the next SaveToken re-encrypts under the new key.
        foreach (var key in GetDecryptKeys(configDir))
        {
            try
            {
                var plaintext = new byte[ciphertext.Length];
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                return Encoding.UTF8.GetString(plaintext);
            }
            catch (CryptographicException) { /* try next candidate key */ }
        }
        return null;
    }

    private static byte[] GetEncryptKey(string configDir)
    {
        var userKey = Environment.GetEnvironmentVariable(KeyEnvVar);
        return !string.IsNullOrWhiteSpace(userKey)
            ? DeriveKey("user-key|" + userKey)
            : DeriveKey(LegacyIdentity(configDir));
    }

    private static IEnumerable<byte[]> GetDecryptKeys(string configDir)
    {
        var userKey = Environment.GetEnvironmentVariable(KeyEnvVar);
        if (!string.IsNullOrWhiteSpace(userKey))
            yield return DeriveKey("user-key|" + userKey);
        yield return DeriveKey(LegacyIdentity(configDir));
    }

    private static string LegacyIdentity(string configDir) =>
        GetMachineIdentity(configDir) + "|" + Path.GetFullPath(configDir);

    private static byte[] DeriveKey(string identity) =>
        _keyCache.GetOrAdd(identity, id =>
        {
            var salt = Encoding.UTF8.GetBytes("lancache-prefill-v1");
            return Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(id), salt, 100_000, HashAlgorithmName.SHA256, KeySize);
        });

    private static string GetMachineIdentity(string configDir)
    {
        // Persist a stable per-install identity in the config volume. This keeps the
        // derived key constant across container recreation (where /etc/machine-id or
        // the container hostname can change), which would otherwise render the stored
        // token undecryptable and force a needless re-login. On first run we seed it
        // from the legacy identity (machine-id, else hostname) so any token already
        // encrypted under the old scheme stays decryptable.
        try
        {
            var idFile = Path.Combine(configDir, ".machine-id");
            if (File.Exists(idFile))
            {
                var existing = File.ReadAllText(idFile).Trim();
                if (!string.IsNullOrEmpty(existing)) return existing;
            }

            string seed = Environment.MachineName;
            if (File.Exists("/etc/machine-id"))
            {
                var m = File.ReadAllText("/etc/machine-id").Trim();
                if (!string.IsNullOrEmpty(m)) seed = m;
            }

            File.WriteAllText(idFile, seed);
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(idFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return seed;
        }
        catch
        {
            // Fall back to the legacy behavior if the config dir isn't writable.
            if (File.Exists("/etc/machine-id"))
            {
                var m = File.ReadAllText("/etc/machine-id").Trim();
                if (!string.IsNullOrEmpty(m)) return m;
            }
            return Environment.MachineName;
        }
    }
}
