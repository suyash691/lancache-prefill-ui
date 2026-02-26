using System.Security.Cryptography;
using System.Text;

namespace LancachePrefill;

public static class TokenProtection
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static byte[]? _cachedKey;
    private static string? _cachedKeyDir;

    public static string Encrypt(string plaintext, string configDir)
    {
        var key = GetKey(configDir);
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
        try
        {
            var combined = Convert.FromBase64String(encoded);
            if (combined.Length < NonceSize + TagSize) return null;

            var key = GetKey(configDir);
            var nonce = combined[..NonceSize];
            var tag = combined[NonceSize..(NonceSize + TagSize)];
            var ciphertext = combined[(NonceSize + TagSize)..];
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException) { return null; }
    }

    private static byte[] GetKey(string configDir)
    {
        var fullPath = Path.GetFullPath(configDir);
        if (_cachedKey != null && _cachedKeyDir == fullPath) return _cachedKey;

        var identity = GetMachineIdentity() + "|" + fullPath;
        var salt = Encoding.UTF8.GetBytes("lancache-prefill-v1");
        _cachedKey = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(identity), salt, 100_000, HashAlgorithmName.SHA256, KeySize);
        _cachedKeyDir = fullPath;
        return _cachedKey;
    }

    private static string GetMachineIdentity()
    {
        if (File.Exists("/etc/machine-id"))
            return File.ReadAllText("/etc/machine-id").Trim();
        return Environment.MachineName;
    }
}
