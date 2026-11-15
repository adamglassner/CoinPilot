using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using WillowWallet.Sync;

namespace WillowWallet.Server;

public sealed record UserRow(long Id, string Username, bool Disabled, DateTime CreatedAt, DateTime? LastSeenAt, long Seq);

public sealed record SessionUser(long UserId, string Username, string SessionId);

public sealed record UserSummary(long Id, string Username, bool Disabled, DateTime CreatedAt, DateTime? LastSeenAt, long Records, long Files, long FileBytes, int Devices);

public sealed record DeviceRow(string Id, long UserId, string Username, string Name, string Platform, string AppVersion, string? Ip, DateTime CreatedAt, DateTime LastSeen);

public sealed record InviteRow(string Code, string? Username, string? Note, int UsesLeft, DateTime CreatedAt, DateTime? ExpiresAt, string? UsedBy);

/// <summary>A request the server refuses, with a reason the app can show.</summary>
public sealed class ApiProblem(int status, string error, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Error { get; } = error;
}

/// <summary>Sync accounts: sign-up, sign-in, recovery, sessions (one per device).</summary>
public sealed class Accounts(Db db, SettingsStore settings, ActivityLog activity)
{
    private static readonly TimeSpan SessionLife = TimeSpan.FromDays(180);

    public PreloginResponse Prelogin(string username)
    {
        string? name = Protocol.NormalizeUsername(username);
        if (name != null)
        {
            using var c = db.Open();
            var found = Db.Query(c, "SELECT kdf_salt, kdf_iterations FROM users WHERE username = $name",
                r => new PreloginResponse(r.GetString(0), r.GetInt32(1)), new { name });
            if (found.Count > 0) return found[0];
        }
        // Same answer every time for an unknown name, so this can't be used to find out who has an account.
        var fake = HMACSHA256.HashData(settings.InstanceSecret, Encoding.UTF8.GetBytes("salt:" + (name ?? username)));
        return new PreloginResponse(Convert.ToBase64String(fake[..16]), SyncCrypto.DefaultIterations);
    }

    public SessionResponse Register(RegisterRequest r, string? ip)
    {
        string name = Protocol.NormalizeUsername(r.Username)
            ?? throw new ApiProblem(400, "bad_username", "Usernames are 3 to 32 characters: letters, numbers, dots, dashes and underscores.");
        var auth = Decode(r.AuthSecret, 32, "authSecret");
        var recovery = Decode(r.RecoveryAuthSecret, 32, "recoveryAuthSecret");
        Decode(r.KdfSalt, 16, "kdfSalt");
        if (r.Iterations is < 100_000 or > 10_000_000) throw new ApiProblem(400, "bad_request", "Unsupported key settings.");
        RequireSealed(r.WrappedKey, "wrappedKey");
        RequireSealed(r.RecoveryWrappedKey, "recoveryWrappedKey");

        using var c = db.Open();
        using var tx = c.BeginTransaction();
        if (Db.Scalar(c, "SELECT 1 FROM users WHERE username = $name", new { name }, tx) != null)
            throw new ApiProblem(409, "username_taken", "That username is taken. Pick another one.");
        int max = settings.MaxUsers;
        if (max > 0 && (long)Db.Scalar(c, "SELECT COUNT(*) FROM users", null, tx)! >= max)
            throw new ApiProblem(403, "server_full", "This server has reached its limit of accounts. Ask the person who runs it.");

        string? inviteUsed = null;
        if (!string.IsNullOrWhiteSpace(r.InviteCode))
        {
            string code = r.InviteCode.Trim().ToUpperInvariant();
            var invite = Db.Query(c, "SELECT code, username, uses_left, expires_at FROM invites WHERE code = $code",
                x => (Code: x.GetString(0), Username: x.IsDBNull(1) ? null : x.GetString(1), Uses: x.GetInt32(2), Expires: Db.Date(x, 3)), new { code }, tx);
            if (invite.Count == 0 || invite[0].Uses <= 0 || invite[0].Expires < DateTime.UtcNow)
                throw new ApiProblem(403, "bad_invite", "That invite code isn't valid (it may have been used or expired).");
            if (invite[0].Username != null && !string.Equals(invite[0].Username, name, StringComparison.OrdinalIgnoreCase))
                throw new ApiProblem(403, "bad_invite", $"That invite is for the username \"{invite[0].Username}\".");
            Db.Exec(c, "UPDATE invites SET uses_left = uses_left - 1, used_by = TRIM(COALESCE(used_by || ', ', '') || $name) WHERE code = $code", new { code, name }, tx);
            inviteUsed = code;
        }
        else if (!settings.OpenSignup)
        {
            throw new ApiProblem(403, "invite_required", "This server is invite-only. Ask the person who runs it for an invite code.");
        }

        var authSalt = RandomNumberGenerator.GetBytes(16);
        var recoverySalt = RandomNumberGenerator.GetBytes(16);
        Db.Exec(c, """
            INSERT INTO users(username, auth_hash, auth_salt, kdf_salt, kdf_iterations, wrapped_key, recovery_hash, recovery_salt, recovery_wrapped_key, created_at, last_seen_at)
            VALUES($name, $ah, $as, $ks, $it, $wk, $rh, $rs, $rwk, $now, $now)
            """, new
        {
            name, ah = Secrets.HashSecret(auth, authSalt), @as = authSalt, ks = r.KdfSalt, it = r.Iterations, wk = r.WrappedKey,
            rh = Secrets.HashSecret(recovery, recoverySalt), rs = recoverySalt, rwk = r.RecoveryWrappedKey, now = Db.Now(),
        }, tx);
        long id = (long)Db.Scalar(c, "SELECT last_insert_rowid()", null, tx)!;
        string token = NewSession(c, tx, id, r.Device, ip);
        tx.Commit();
        activity.Add("signup", name, inviteUsed != null ? $"Created an account with invite {inviteUsed} on {DeviceText(r.Device)}" : $"Created an account on {DeviceText(r.Device)}");
        return new SessionResponse(token, name, r.WrappedKey, r.KdfSalt, r.Iterations, 0);
    }

    public SessionResponse Login(LoginRequest r, string? ip)
    {
        var user = FindForAuth(r.Username);
        var auth = Decode(r.AuthSecret, 32, "authSecret");
        if (user == null || !Secrets.Equal(Secrets.HashSecret(auth, user.AuthSalt), user.AuthHash))
            throw new ApiProblem(401, "bad_login", "Wrong username or password.");
        if (user.Disabled) throw new ApiProblem(403, "disabled", "This account has been turned off by the server's admin.");
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        string token = NewSession(c, tx, user.Id, r.Device, ip);
        tx.Commit();
        activity.Add("signin", user.Username, $"Signed in on {DeviceText(r.Device)}");
        return new SessionResponse(token, user.Username, user.WrappedKey, user.KdfSalt, user.Iterations, user.Seq);
    }

    public RecoveryStartResponse RecoveryStart(RecoveryStartRequest r)
    {
        var user = CheckRecovery(r.Username, r.RecoveryAuthSecret);
        return new RecoveryStartResponse(user.RecoveryWrappedKey);
    }

    public SessionResponse RecoveryFinish(RecoveryFinishRequest r, string? ip)
    {
        var user = CheckRecovery(r.Username, r.RecoveryAuthSecret);
        var auth = Decode(r.NewAuthSecret, 32, "newAuthSecret");
        Decode(r.KdfSalt, 16, "kdfSalt");
        RequireSealed(r.WrappedKey, "wrappedKey");
        var salt = RandomNumberGenerator.GetBytes(16);
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        Db.Exec(c, "UPDATE users SET auth_hash = $h, auth_salt = $s, kdf_salt = $ks, kdf_iterations = $it, wrapped_key = $wk WHERE id = $id",
            new { h = Secrets.HashSecret(auth, salt), s = salt, ks = r.KdfSalt, it = r.Iterations, wk = r.WrappedKey, id = user.Id }, tx);
        // A new password signs out every other device
        Db.Exec(c, "DELETE FROM sessions WHERE user_id = $id", new { id = user.Id }, tx);
        string token = NewSession(c, tx, user.Id, r.Device, ip);
        tx.Commit();
        activity.Add("recovery", user.Username, $"Set a new password with the recovery key on {DeviceText(r.Device)}");
        return new SessionResponse(token, user.Username, r.WrappedKey, r.KdfSalt, r.Iterations, user.Seq);
    }

    public void ChangePassword(SessionUser who, ChangePasswordRequest r)
    {
        var user = FindForAuth(who.Username)!;
        if (!Secrets.Equal(Secrets.HashSecret(Decode(r.CurrentAuthSecret, 32, "currentAuthSecret"), user.AuthSalt), user.AuthHash))
            throw new ApiProblem(403, "bad_password", "Your current password isn't right.");
        var auth = Decode(r.NewAuthSecret, 32, "newAuthSecret");
        Decode(r.KdfSalt, 16, "kdfSalt");
        RequireSealed(r.WrappedKey, "wrappedKey");
        var salt = RandomNumberGenerator.GetBytes(16);
        using var c = db.Open();
        Db.Exec(c, "UPDATE users SET auth_hash = $h, auth_salt = $s, kdf_salt = $ks, kdf_iterations = $it, wrapped_key = $wk WHERE id = $id",
            new { h = Secrets.HashSecret(auth, salt), s = salt, ks = r.KdfSalt, it = r.Iterations, wk = r.WrappedKey, id = user.Id });
        Db.Exec(c, "DELETE FROM sessions WHERE user_id = $id AND id <> $sid", new { id = user.Id, sid = who.SessionId });
        activity.Add("password", user.Username, "Changed their password (other devices were signed out)");
    }

    /// <summary>The signed-in user for a bearer token, or null. Also notes when the device was last seen.</summary>
    public SessionUser? Authenticate(string token, string? ip)
    {
        using var c = db.Open();
        var hash = Secrets.TokenHash(token);
        var found = Db.Query(c, """
            SELECT s.id, u.id, u.username, u.disabled, s.last_seen_at FROM sessions s JOIN users u ON u.id = s.user_id WHERE s.token_hash = $hash
            """, r => (Sid: r.GetString(0), Uid: r.GetInt64(1), Name: r.GetString(2), Disabled: r.GetInt64(3) != 0, Seen: Db.Date(r, 4)!.Value), new { hash });
        if (found.Count == 0) return null;
        var s = found[0];
        if (s.Disabled || s.Seen < DateTime.UtcNow - SessionLife) return null;
        if (s.Seen < DateTime.UtcNow.AddMinutes(-1))
        {
            Db.Exec(c, "UPDATE sessions SET last_seen_at = $now, ip = $ip WHERE id = $sid", new { now = Db.Now(), ip, sid = s.Sid });
            Db.Exec(c, "UPDATE users SET last_seen_at = $now WHERE id = $uid", new { now = Db.Now(), uid = s.Uid });
        }
        return new SessionUser(s.Uid, s.Name, s.Sid);
    }

    public void Logout(SessionUser who)
    {
        using var c = db.Open();
        Db.Exec(c, "DELETE FROM sessions WHERE id = $sid", new { sid = who.SessionId });
    }

    public AccountResponse Account(SessionUser who)
    {
        using var c = db.Open();
        var info = Db.Query(c, """
            SELECT u.created_at,
                   (SELECT COUNT(*) FROM records r WHERE r.user_id = u.id AND r.deleted = 0),
                   (SELECT COALESCE(SUM(size), 0) FROM files f WHERE f.user_id = u.id)
            FROM users u WHERE u.id = $id
            """, r => (Created: Db.Date(r, 0)!.Value, Records: r.GetInt64(1), Bytes: r.GetInt64(2)), new { id = who.UserId })[0];
        var devices = Devices(c, who.UserId).Select(d => new DeviceSummary(d.Id, d.Name, d.Platform, d.AppVersion, d.LastSeen, d.Id == who.SessionId)).ToList();
        return new AccountResponse(who.Username, info.Created, info.Records, info.Bytes, devices);
    }

    public bool SignOutDevice(SessionUser who, string deviceId)
    {
        using var c = db.Open();
        return Db.Exec(c, "DELETE FROM sessions WHERE id = $deviceId AND user_id = $uid", new { deviceId, uid = who.UserId }) > 0;
    }

    // ----- Admin -----

    public List<UserSummary> List()
    {
        using var c = db.Open();
        return Db.Query(c, """
            SELECT u.id, u.username, u.disabled, u.created_at, u.last_seen_at,
                   (SELECT COUNT(*) FROM records r WHERE r.user_id = u.id AND r.deleted = 0),
                   (SELECT COUNT(*) FROM files f WHERE f.user_id = u.id),
                   (SELECT COALESCE(SUM(size), 0) FROM files f WHERE f.user_id = u.id),
                   (SELECT COUNT(*) FROM sessions s WHERE s.user_id = u.id)
            FROM users u ORDER BY u.username
            """, r => new UserSummary(r.GetInt64(0), r.GetString(1), r.GetInt64(2) != 0, Db.Date(r, 3)!.Value, Db.Date(r, 4),
                r.GetInt64(5), r.GetInt64(6), r.GetInt64(7), (int)r.GetInt64(8)));
    }

    public List<DeviceRow> AllDevices(long? userId = null)
    {
        using var c = db.Open();
        return Devices(c, userId);
    }

    private static List<DeviceRow> Devices(SqliteConnection c, long? userId) => Db.Query(c, """
        SELECT s.id, s.user_id, u.username, s.device_name, s.platform, s.app_version, s.ip, s.created_at, s.last_seen_at
        FROM sessions s JOIN users u ON u.id = s.user_id WHERE ($uid IS NULL OR s.user_id = $uid) ORDER BY s.last_seen_at DESC
        """, r => new DeviceRow(r.GetString(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
            r.IsDBNull(6) ? null : r.GetString(6), Db.Date(r, 7)!.Value, Db.Date(r, 8)!.Value), new { uid = userId });

    public string? NameOf(long id)
    {
        using var c = db.Open();
        return Db.Scalar(c, "SELECT username FROM users WHERE id = $id", new { id }) as string;
    }

    public void SetDisabled(long id, bool disabled)
    {
        using var c = db.Open();
        Db.Exec(c, "UPDATE users SET disabled = $d WHERE id = $id", new { d = disabled ? 1 : 0, id });
        if (disabled) Db.Exec(c, "DELETE FROM sessions WHERE user_id = $id", new { id });
    }

    public void SignOutEverywhere(long id)
    {
        using var c = db.Open();
        Db.Exec(c, "DELETE FROM sessions WHERE user_id = $id", new { id });
    }

    public void RevokeDevice(string deviceId)
    {
        using var c = db.Open();
        Db.Exec(c, "DELETE FROM sessions WHERE id = $deviceId", new { deviceId });
    }

    /// <summary>Deletes the account, its synced data and files.</summary>
    public void Delete(long id, SyncStore sync)
    {
        sync.DeleteAllFiles(id);
        using var c = db.Open();
        Db.Exec(c, "DELETE FROM users WHERE id = $id", new { id });
    }

    // ----- Invites -----

    public InviteRow CreateInvite(string? username, string? note, int uses, int? days)
    {
        string? name = string.IsNullOrWhiteSpace(username) ? null
            : Protocol.NormalizeUsername(username) ?? throw new ApiProblem(400, "bad_username", "Usernames are 3 to 32 characters: letters, numbers, dots, dashes and underscores.");
        var invite = new InviteRow(Secrets.NewInviteCode(), name, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), Math.Clamp(uses, 1, 1000),
            DateTime.UtcNow, days is > 0 ? DateTime.UtcNow.AddDays(days.Value) : null, null);
        using var c = db.Open();
        Db.Exec(c, "INSERT INTO invites(code, username, note, uses_left, created_at, expires_at) VALUES($code, $name, $note, $uses, $now, $exp)",
            new { code = invite.Code, name, note = invite.Note, uses = invite.UsesLeft, now = Db.Now(), exp = invite.ExpiresAt?.ToString("O") });
        return invite;
    }

    public List<InviteRow> Invites()
    {
        using var c = db.Open();
        return Db.Query(c, "SELECT code, username, note, uses_left, created_at, expires_at, used_by FROM invites ORDER BY created_at DESC",
            r => new InviteRow(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetInt32(3),
                Db.Date(r, 4)!.Value, Db.Date(r, 5), r.IsDBNull(6) ? null : r.GetString(6)));
    }

    public void DeleteInvite(string code)
    {
        using var c = db.Open();
        Db.Exec(c, "DELETE FROM invites WHERE code = $code", new { code });
    }

    // ----- Helpers -----

    private sealed record AuthUser(long Id, string Username, byte[] AuthHash, byte[] AuthSalt, string KdfSalt, int Iterations, string WrappedKey,
        byte[] RecoveryHash, byte[] RecoverySalt, string RecoveryWrappedKey, bool Disabled, long Seq);

    private AuthUser? FindForAuth(string username)
    {
        string? name = Protocol.NormalizeUsername(username);
        if (name == null) return null;
        using var c = db.Open();
        var found = Db.Query(c, """
            SELECT id, username, auth_hash, auth_salt, kdf_salt, kdf_iterations, wrapped_key, recovery_hash, recovery_salt, recovery_wrapped_key, disabled, seq
            FROM users WHERE username = $name
            """, r => new AuthUser(r.GetInt64(0), r.GetString(1), (byte[])r[2], (byte[])r[3], r.GetString(4), r.GetInt32(5), r.GetString(6),
                (byte[])r[7], (byte[])r[8], r.GetString(9), r.GetInt64(10) != 0, r.GetInt64(11)), new { name });
        return found.FirstOrDefault();
    }

    private AuthUser CheckRecovery(string username, string recoveryAuthSecret)
    {
        var user = FindForAuth(username);
        var secret = Decode(recoveryAuthSecret, 32, "recoveryAuthSecret");
        if (user == null || !Secrets.Equal(Secrets.HashSecret(secret, user.RecoverySalt), user.RecoveryHash))
            throw new ApiProblem(401, "bad_recovery", "That username and recovery key don't match.");
        if (user.Disabled) throw new ApiProblem(403, "disabled", "This account has been turned off by the server's admin.");
        return user;
    }

    private static string NewSession(SqliteConnection c, SqliteTransaction tx, long userId, DeviceInfo device, string? ip)
    {
        string token = Secrets.NewToken();
        string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        Db.Exec(c, """
            INSERT INTO sessions(id, token_hash, user_id, device_name, platform, app_version, ip, created_at, last_seen_at)
            VALUES($id, $hash, $userId, $name, $platform, $version, $ip, $now, $now)
            """, new
        {
            id, hash = Secrets.TokenHash(token), userId, name = Trim(device?.Name, 60) ?? "Device", platform = Trim(device?.Platform, 30) ?? "",
            version = Trim(device?.AppVersion, 30) ?? "", ip, now = Db.Now(),
        }, tx);
        return token;
    }

    private static string? Trim(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length > max ? s.Trim()[..max] : s.Trim();
    private static string DeviceText(DeviceInfo d) => $"{d?.Name ?? "a device"} ({d?.Platform} {d?.AppVersion})".Replace(" ()", "").Replace("( ", "(");

    private static byte[] Decode(string? b64, int length, string field)
    {
        try
        {
            var bytes = Convert.FromBase64String(b64 ?? "");
            if (bytes.Length == length) return bytes;
        }
        catch (FormatException) { }
        throw new ApiProblem(400, "bad_request", $"Missing or invalid {field}.");
    }

    private static void RequireSealed(string? value, string field)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith("v1.") || value.Length > 1000) throw new ApiProblem(400, "bad_request", $"Missing or invalid {field}.");
    }
}
