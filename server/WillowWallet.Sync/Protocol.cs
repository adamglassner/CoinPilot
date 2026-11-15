namespace WillowWallet.Sync;

// Messages of the WillowWallet sync API (JSON, camelCase). See server/PROTOCOL.md.

public sealed record ServerInfo(string Name, string Version, bool OpenSignup, bool Syncing, int ProtocolVersion);

public sealed record DeviceInfo(string Name, string Platform, string AppVersion);

public sealed record PreloginRequest(string Username);
public sealed record PreloginResponse(string KdfSalt, int Iterations);

public sealed record RegisterRequest(
    string Username,
    string? InviteCode,
    string AuthSecret,
    string KdfSalt,
    int Iterations,
    string WrappedKey,
    string RecoveryAuthSecret,
    string RecoveryWrappedKey,
    DeviceInfo Device);

public sealed record LoginRequest(string Username, string AuthSecret, DeviceInfo Device);

/// <summary>Returned by register, login and recovery: a session token plus what the device needs to open the data key.</summary>
public sealed record SessionResponse(string Token, string Username, string WrappedKey, string KdfSalt, int Iterations, long LatestSeq);

public sealed record RecoveryStartRequest(string Username, string RecoveryAuthSecret);
public sealed record RecoveryStartResponse(string RecoveryWrappedKey);

public sealed record RecoveryFinishRequest(
    string Username,
    string RecoveryAuthSecret,
    string NewAuthSecret,
    string KdfSalt,
    int Iterations,
    string WrappedKey,
    DeviceInfo Device);

public sealed record ChangePasswordRequest(string CurrentAuthSecret, string NewAuthSecret, string KdfSalt, int Iterations, string WrappedKey);

public sealed record AccountResponse(string Username, DateTime CreatedAt, long Records, long FileBytes, List<DeviceSummary> Devices);
public sealed record DeviceSummary(string Id, string Name, string Platform, string AppVersion, DateTime LastSeen, bool IsThisDevice);

/// <summary>One record as stored on the server: identity, server sequence number, last change (UTC ms) and the sealed payload.</summary>
public sealed record Change(string Collection, string Id, long Seq, long Modified, bool Deleted, string? Payload);

public sealed record ChangesResponse(List<Change> Changes, long Latest, bool More);

public sealed record PushChange(string Collection, string Id, long Modified, bool Deleted, string? Payload);
public sealed record PushRequest(List<PushChange> Changes);

/// <summary>"ok" (stored, with its new seq) or "stale" (the server already has a newer version; pull it).</summary>
public sealed record PushResult(string Collection, string Id, string Status, long Seq);
public sealed record PushResponse(List<PushResult> Results, long Latest);

public sealed record ErrorResponse(string Error, string Message);

public static class Protocol
{
    public const int Version = 1;
    public const int MaxPushBatch = 500;
    public const int MaxPullBatch = 1000;
    public const long MaxFileBytes = 50L * 1024 * 1024;
    public const int MaxPayloadChars = 2_000_000;

    /// <summary>3–32 characters: lowercase letters, digits, dot, dash and underscore.</summary>
    public static string? NormalizeUsername(string? name)
    {
        string n = (name ?? "").Trim().ToLowerInvariant();
        if (n.Length is < 3 or > 32) return null;
        return n.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-' or '_') ? n : null;
    }

    /// <summary>Collection names: lowercase letters, digits and dashes, up to 40 characters.</summary>
    public static bool IsValidCollection(string? c) =>
        !string.IsNullOrEmpty(c) && c.Length <= 40 && c.All(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    /// <summary>Record ids and file names: letters, digits, dot, dash, underscore and slash; up to 200 characters.</summary>
    public static bool IsValidId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 200 && !id.Contains("..") &&
        id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_' or '/');
}
