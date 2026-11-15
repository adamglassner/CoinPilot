using System.Security.Cryptography;
using System.Text;

namespace WillowWallet.Sync;

/// <summary>A signed-in device: its session and the opened data key (never sent to the server).</summary>
public sealed record SignedIn(SessionResponse Session, byte[] DataKey);

/// <summary>Account steps every app does the same way: create, sign in, recover with the recovery key, change password.</summary>
public static class SyncAccount
{
    /// <summary>Creates an account. Returns the session, the data key and the recovery key text to show once.</summary>
    public static async Task<(SignedIn SignedIn, string RecoveryKey)> RegisterAsync(SyncApiClient api, string username, string password,
        string? inviteCode, DeviceInfo device, int iterations = SyncCrypto.DefaultIterations, CancellationToken ct = default)
    {
        string name = Protocol.NormalizeUsername(username)
            ?? throw new SyncApiException(null, "bad_username", "Usernames are 3 to 32 characters: letters, numbers, dots, dashes and underscores.");
        CheckPassword(password);
        var salt = SyncCrypto.RandomBytes(16);
        var creds = await Task.Run(() => SyncCrypto.DeriveFromPassword(password, salt, iterations), ct);
        var dataKey = SyncCrypto.RandomBytes(32);
        var (recoveryBytes, recoveryText) = SyncCrypto.NewRecoveryKey();
        var recovery = SyncCrypto.DeriveFromRecoveryKey(recoveryBytes);
        var session = await api.RegisterAsync(new RegisterRequest(
            name, string.IsNullOrWhiteSpace(inviteCode) ? null : inviteCode.Trim(),
            Convert.ToBase64String(creds.AuthSecret), Convert.ToBase64String(salt), iterations,
            SyncCrypto.Seal(creds.KeyEncryptionKey, dataKey, SyncCrypto.KeyAad),
            Convert.ToBase64String(recovery.AuthSecret),
            SyncCrypto.Seal(recovery.KeyEncryptionKey, dataKey, SyncCrypto.KeyAad),
            device), ct);
        api.Token = session.Token;
        return (new SignedIn(session, dataKey), recoveryText);
    }

    public static async Task<SignedIn> LoginAsync(SyncApiClient api, string username, string password, DeviceInfo device, CancellationToken ct = default)
    {
        var pre = await api.PreloginAsync(username.Trim(), ct);
        var creds = await Task.Run(() => SyncCrypto.DeriveFromPassword(password, Convert.FromBase64String(pre.KdfSalt), pre.Iterations), ct);
        var session = await api.LoginAsync(new LoginRequest(username.Trim(), Convert.ToBase64String(creds.AuthSecret), device), ct);
        api.Token = session.Token;
        try
        {
            return new SignedIn(session, SyncCrypto.Open(creds.KeyEncryptionKey, session.WrappedKey, SyncCrypto.KeyAad));
        }
        catch (CryptographicException)
        {
            throw new SyncApiException(null, "bad_key", "Signed in, but your data key couldn't be opened. Try signing in again.");
        }
    }

    /// <summary>Forgot the password: the recovery key opens the data key, which is then locked with a new password.</summary>
    public static async Task<SignedIn> RecoverAsync(SyncApiClient api, string username, string recoveryKeyText, string newPassword, DeviceInfo device,
        int iterations = SyncCrypto.DefaultIterations, CancellationToken ct = default)
    {
        var recoveryBytes = SyncCrypto.ParseRecoveryKey(recoveryKeyText)
            ?? throw new SyncApiException(null, "bad_recovery", "That isn't a recovery key. It looks like ABCD-EFGH-… (32 letters and numbers).");
        CheckPassword(newPassword);
        var recovery = SyncCrypto.DeriveFromRecoveryKey(recoveryBytes);
        var start = await api.RecoveryStartAsync(new RecoveryStartRequest(username.Trim(), Convert.ToBase64String(recovery.AuthSecret)), ct);
        byte[] dataKey;
        try { dataKey = SyncCrypto.Open(recovery.KeyEncryptionKey, start.RecoveryWrappedKey, SyncCrypto.KeyAad); }
        catch (CryptographicException) { throw new SyncApiException(null, "bad_recovery", "That recovery key doesn't open this account's data."); }
        var salt = SyncCrypto.RandomBytes(16);
        var creds = await Task.Run(() => SyncCrypto.DeriveFromPassword(newPassword, salt, iterations), ct);
        var session = await api.RecoveryFinishAsync(new RecoveryFinishRequest(username.Trim(), Convert.ToBase64String(recovery.AuthSecret),
            Convert.ToBase64String(creds.AuthSecret), Convert.ToBase64String(salt), iterations,
            SyncCrypto.Seal(creds.KeyEncryptionKey, dataKey, SyncCrypto.KeyAad), device), ct);
        api.Token = session.Token;
        return new SignedIn(session, dataKey);
    }

    public static async Task ChangePasswordAsync(SyncApiClient api, string username, string currentPassword, string newPassword, byte[] dataKey,
        int iterations = SyncCrypto.DefaultIterations, CancellationToken ct = default)
    {
        CheckPassword(newPassword);
        var pre = await api.PreloginAsync(username, ct);
        var current = await Task.Run(() => SyncCrypto.DeriveFromPassword(currentPassword, Convert.FromBase64String(pre.KdfSalt), pre.Iterations), ct);
        var salt = SyncCrypto.RandomBytes(16);
        var next = await Task.Run(() => SyncCrypto.DeriveFromPassword(newPassword, salt, iterations), ct);
        await api.ChangePasswordAsync(new ChangePasswordRequest(Convert.ToBase64String(current.AuthSecret), Convert.ToBase64String(next.AuthSecret),
            Convert.ToBase64String(salt), iterations, SyncCrypto.Seal(next.KeyEncryptionKey, dataKey, SyncCrypto.KeyAad)), ct);
    }

    /// <summary>At least 8 characters. (The server never sees it, so this is the only check.)</summary>
    public static void CheckPassword(string password)
    {
        if ((password ?? "").Length < 8) throw new SyncApiException(null, "weak_password", "Use at least 8 characters for your password.");
    }

    public static string SealRecord(byte[] dataKey, string collection, string id, string json) =>
        SyncCrypto.Seal(dataKey, Encoding.UTF8.GetBytes(json), SyncCrypto.RecordAad(collection, id));

    public static string OpenRecord(byte[] dataKey, string collection, string id, string payload) =>
        Encoding.UTF8.GetString(SyncCrypto.Open(dataKey, payload, SyncCrypto.RecordAad(collection, id)));
}
