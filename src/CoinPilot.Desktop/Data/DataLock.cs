using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CoinPilot.Data;

/// <summary>
/// Optional PIN/password protection for everything in the data folder.
///
/// When turned on, a random 256-bit data key encrypts every data file, receipt and tax document with AES-GCM.
/// The data key itself is stored in lock.json twice: once encrypted with a key derived from your PIN/password
/// (PBKDF2-SHA256, 600,000 rounds) and once with a key derived from a recovery key shown when you turn it on.
/// Without one of those, the files can't be read. WillowWallet never stores the PIN/password itself.
/// </summary>
public static class DataLock
{
    private static readonly byte[] Magic = "CPENC1"u8.ToArray();
    private const int Iterations = 600_000;
    private const int NonceSize = 12, TagSize = 16;

    private static byte[]? _key;

    public sealed class LockInfo
    {
        public int Version { get; set; } = 1;
        /// <summary>"PIN" or "Password".</summary>
        public string Kind { get; set; } = "Password";
        public string Salt { get; set; } = "";
        public int Iterations { get; set; } = DataLock.Iterations;
        public string WrappedKey { get; set; } = "";
        public string RecoverySalt { get; set; } = "";
        public string RecoveryWrappedKey { get; set; } = "";
        /// <summary>Minutes without activity before WillowWallet locks itself (0 = never).</summary>
        public int AutoLockMinutes { get; set; } = 15;
    }

    public static string LockFile => Path.Combine(DataStore.Folder, "lock.json");

    /// <summary>True when the data is protected with a PIN/password.</summary>
    public static bool IsEnabled => File.Exists(LockFile);

    /// <summary>True when the data can be read: protection is off, or it's on and unlocked.</summary>
    public static bool IsOpen => !IsEnabled || _key != null;

    public static LockInfo? Info
    {
        get
        {
            try { return IsEnabled ? JsonSerializer.Deserialize<LockInfo>(File.ReadAllText(LockFile)) : null; }
            catch { return null; }
        }
    }

    // ----- Reading and writing data files -----

    public static bool IsEncrypted(byte[] bytes) => bytes.Length >= Magic.Length && bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    public static byte[] ReadBytes(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (!IsEncrypted(bytes)) return bytes;
        if (_key == null) throw new InvalidOperationException("WillowWallet is locked.");
        return Decrypt(bytes, _key);
    }

    public static void WriteBytes(string path, byte[] data) =>
        File.WriteAllBytes(path, _key != null ? Encrypt(data, _key) : data);

    public static string ReadText(string path) => Encoding.UTF8.GetString(ReadBytes(path));

    public static void WriteText(string path, string text) => WriteBytes(path, Encoding.UTF8.GetBytes(text));

    private static byte[] Encrypt(byte[] plain, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize)) aes.Encrypt(nonce, plain, cipher, tag);
        var output = new byte[Magic.Length + NonceSize + TagSize + cipher.Length];
        Magic.CopyTo(output, 0);
        nonce.CopyTo(output, Magic.Length);
        tag.CopyTo(output, Magic.Length + NonceSize);
        cipher.CopyTo(output, Magic.Length + NonceSize + TagSize);
        return output;
    }

    private static byte[] Decrypt(byte[] data, byte[] key)
    {
        int offset = Magic.Length;
        var nonce = data.AsSpan(offset, NonceSize);
        var tag = data.AsSpan(offset + NonceSize, TagSize);
        var cipher = data.AsSpan(offset + NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    // ----- Keys -----

    private static byte[] Derive(string secret, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), salt, iterations, HashAlgorithmName.SHA256, 32);

    private static string Wrap(byte[] dataKey, byte[] wrappingKey) => Convert.ToBase64String(Encrypt(dataKey, wrappingKey));

    private static byte[]? Unwrap(string wrapped, byte[] wrappingKey)
    {
        try { return Decrypt(Convert.FromBase64String(wrapped), wrappingKey); }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException) { return null; }
    }

    /// <summary>"ABCDE-FGHJK-LMNPQ-RSTUV-WXYZ2": 25 letters and digits, easy to copy down.</summary>
    private static string NewRecoveryKey()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = Enumerable.Range(0, 25).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray();
        return string.Join("-", Enumerable.Range(0, 5).Select(i => new string(chars, i * 5, 5)));
    }

    private static string NormalizeRecovery(string key) =>
        new string(key.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static void SaveInfo(LockInfo info) =>
        File.WriteAllText(LockFile, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>A PIN is 4–12 digits; a password is at least 6 characters.</summary>
    public static string? Validate(string secret, string kind) => kind == "PIN"
        ? (secret.Length is >= 4 and <= 12 && secret.All(char.IsDigit) ? null : "A PIN is 4 to 12 digits.")
        : (secret.Length >= 6 ? null : "Use at least 6 characters for your password.");

    // ----- Lock / unlock -----

    public static bool Unlock(string secret)
    {
        var info = Info;
        if (info == null) return true;
        var key = Unwrap(info.WrappedKey, Derive(secret, Convert.FromBase64String(info.Salt), info.Iterations));
        if (key == null) return false;
        _key = key;
        return true;
    }

    public static bool UnlockWithRecoveryKey(string recoveryKey)
    {
        var info = Info;
        if (info == null) return true;
        var key = Unwrap(info.RecoveryWrappedKey, Derive(NormalizeRecovery(recoveryKey), Convert.FromBase64String(info.RecoverySalt), info.Iterations));
        if (key == null) return false;
        _key = key;
        return true;
    }

    /// <summary>Forgets the key (the data can't be read until unlocked again) and drops everything loaded.</summary>
    public static void Lock()
    {
        if (!IsEnabled) return;
        if (_key != null) CryptographicOperations.ZeroMemory(_key);
        _key = null;
        FileVault.CleanUpOpenedFiles();
        DataStore.Reload();
    }

    /// <summary>Forgets the key without reloading (after restoring a backup).</summary>
    public static void Forget() => _key = null;

    /// <summary>
    /// Turns protection on: encrypts every file in the data folder and returns the recovery key to show once.
    /// </summary>
    public static string Enable(string secret, string kind, int autoLockMinutes)
    {
        if (IsEnabled) throw new InvalidOperationException("Protection is already on.");
        var dataKey = RandomNumberGenerator.GetBytes(32);
        string recovery = NewRecoveryKey();
        var salt = RandomNumberGenerator.GetBytes(16);
        var recoverySalt = RandomNumberGenerator.GetBytes(16);
        var info = new LockInfo
        {
            Kind = kind,
            Salt = Convert.ToBase64String(salt),
            RecoverySalt = Convert.ToBase64String(recoverySalt),
            WrappedKey = Wrap(dataKey, Derive(secret, salt, Iterations)),
            RecoveryWrappedKey = Wrap(dataKey, Derive(NormalizeRecovery(recovery), recoverySalt, Iterations)),
            AutoLockMinutes = autoLockMinutes,
        };

        Directory.CreateDirectory(DataStore.Folder);
        _key = dataKey;
        RewriteAll(encrypt: true);
        SaveInfo(info);
        return recovery;
    }

    /// <summary>Turns protection off: decrypts every file. Needs the data to be unlocked.</summary>
    public static void Disable()
    {
        if (!IsEnabled) return;
        if (_key == null) throw new InvalidOperationException("Unlock first.");
        RewriteAll(encrypt: false);
        File.Delete(LockFile);
        _key = null;
    }

    /// <summary>Changes the PIN/password (the data key and recovery key stay the same).</summary>
    public static void ChangeSecret(string newSecret, string kind)
    {
        var info = Info ?? throw new InvalidOperationException("Protection is off.");
        if (_key == null) throw new InvalidOperationException("Unlock first.");
        var salt = RandomNumberGenerator.GetBytes(16);
        info.Kind = kind;
        info.Salt = Convert.ToBase64String(salt);
        info.Iterations = Iterations;
        info.WrappedKey = Wrap(_key, Derive(newSecret, salt, Iterations));
        SaveInfo(info);
    }

    public static void SetAutoLock(int minutes)
    {
        var info = Info;
        if (info == null) return;
        info.AutoLockMinutes = minutes;
        SaveInfo(info);
    }

    /// <summary>Re-saves every data file, receipt and tax document encrypted (or plain). Leftover backup copies are removed.</summary>
    private static void RewriteAll(bool encrypt)
    {
        var folder = DataStore.Folder;
        if (!Directory.Exists(folder)) return;
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList())
        {
            string name = Path.GetFileName(path);
            if (string.Equals(name, "lock.json", StringComparison.OrdinalIgnoreCase)) continue;
            if (path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path); // old plain copies
                continue;
            }
            var bytes = File.ReadAllBytes(path);
            bool isEncrypted = IsEncrypted(bytes);
            if (encrypt && !isEncrypted) File.WriteAllBytes(path, Encrypt(bytes, _key!));
            else if (!encrypt && isEncrypted) File.WriteAllBytes(path, Decrypt(bytes, _key!));
        }
    }
}
