using System.Security.Cryptography;
using System.Text;

namespace WillowWallet.Sync;

/// <summary>
/// End-to-end encryption for WillowWallet sync (see server/PROTOCOL.md). The server only ever sees
/// <see cref="Credentials.AuthSecret"/> (to check a sign-in) and sealed data it can't open.
/// </summary>
public static class SyncCrypto
{
    public const int DefaultIterations = 600_000;
    public const string KeyAad = "ww-key-v1";

    /// <summary>Password → (auth secret for the server, key-encryption key that never leaves the device).</summary>
    public static Credentials DeriveFromPassword(string password, byte[] kdfSalt, int iterations)
    {
        byte[] master = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), kdfSalt, iterations, HashAlgorithmName.SHA256, 64);
        return new Credentials(master[..32], master[32..]);
    }

    /// <summary>Recovery key → (auth secret, key-encryption key). The key is random enough that HKDF is plenty.</summary>
    public static Credentials DeriveFromRecoveryKey(byte[] recoveryKey) => new(
        HKDF.DeriveKey(HashAlgorithmName.SHA256, recoveryKey, 32, salt: [], info: Encoding.UTF8.GetBytes("ww-recovery-auth")),
        HKDF.DeriveKey(HashAlgorithmName.SHA256, recoveryKey, 32, salt: [], info: Encoding.UTF8.GetBytes("ww-recovery-kek")));

    public static byte[] RandomBytes(int count) => RandomNumberGenerator.GetBytes(count);

    /// <summary>AES-256-GCM: "v1." + base64(nonce ‖ ciphertext ‖ tag).</summary>
    public static string Seal(byte[] key, byte[] plaintext, string aad, byte[]? nonce = null) =>
        "v1." + Convert.ToBase64String(SealBytes(key, plaintext, aad, nonce));

    public static byte[] Open(byte[] key, string sealedText, string aad)
    {
        if (!sealedText.StartsWith("v1.", StringComparison.Ordinal)) throw new CryptographicException("Unknown format.");
        return OpenBytes(key, Convert.FromBase64String(sealedText[3..]), aad);
    }

    /// <summary>Binary form for files: nonce ‖ ciphertext ‖ tag.</summary>
    public static byte[] SealBytes(byte[] key, byte[] plaintext, string aad, byte[]? nonce = null)
    {
        nonce ??= RandomBytes(12);
        var output = new byte[12 + plaintext.Length + 16];
        nonce.CopyTo(output, 0);
        using var gcm = new AesGcm(key, 16);
        gcm.Encrypt(nonce, plaintext, output.AsSpan(12, plaintext.Length), output.AsSpan(12 + plaintext.Length, 16), Encoding.UTF8.GetBytes(aad));
        return output;
    }

    public static byte[] OpenBytes(byte[] key, byte[] sealedBytes, string aad)
    {
        if (sealedBytes.Length < 28) throw new CryptographicException("Too short.");
        var plaintext = new byte[sealedBytes.Length - 28];
        using var gcm = new AesGcm(key, 16);
        gcm.Decrypt(sealedBytes.AsSpan(0, 12), sealedBytes.AsSpan(12, plaintext.Length), sealedBytes.AsSpan(sealedBytes.Length - 16), plaintext, Encoding.UTF8.GetBytes(aad));
        return plaintext;
    }

    public static string RecordAad(string collection, string id) => $"ww-rec-v1:{collection}:{id}";
    public static string FileAad(string name) => $"ww-file-v1:{name}";

    /// <summary>A new 160-bit recovery key and how it's shown: "GIZT-INJW-…" (base32, groups of four).</summary>
    public static (byte[] Bytes, string Text) NewRecoveryKey()
    {
        var bytes = RandomBytes(20);
        return (bytes, FormatRecoveryKey(bytes));
    }

    public static string FormatRecoveryKey(byte[] bytes)
    {
        string b32 = Base32.Encode(bytes);
        return string.Join("-", Enumerable.Range(0, (b32.Length + 3) / 4).Select(i => b32.Substring(i * 4, Math.Min(4, b32.Length - i * 4))));
    }

    /// <summary>Reads a recovery key however it was typed (spaces, dashes, lower case); null when it isn't one.</summary>
    public static byte[]? ParseRecoveryKey(string text)
    {
        string clean = new(text.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (clean.Length != 32) return null;
        return Base32.Decode(clean);
    }

    /// <summary>What a password or recovery key unlocks: the secret sent to the server and the key kept on the device.</summary>
    public sealed record Credentials(byte[] AuthSecret, byte[] KeyEncryptionKey);
}

public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[]? Decode(string text)
    {
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (char c in text)
        {
            int v = Alphabet.IndexOf(c);
            if (v < 0) return null;
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return output.ToArray();
    }
}
