using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace WillowWallet.Server;

/// <summary>Hashing and random tokens.</summary>
public static class Secrets
{
    /// <summary>For high-entropy secrets sent by apps (auth secrets, recovery secrets): PBKDF2 with a modest count.</summary>
    public static byte[] HashSecret(byte[] secret, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(secret, salt, 10_000, HashAlgorithmName.SHA256, 32);

    /// <summary>For admin passwords typed into the web panel.</summary>
    public static byte[] HashPassword(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);

    public static bool Equal(byte[] a, byte[] b) => CryptographicOperations.FixedTimeEquals(a, b);
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] TokenHash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>"7KQ4-M2XD-9PRT": easy to read out, no 0/O or 1/I.</summary>
    public static string NewInviteCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = Enumerable.Range(0, 12).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray();
        return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}-{new string(chars, 8, 4)}";
    }
}

/// <summary>Settings changed from the web panel (stored in the database).</summary>
public sealed class SettingsStore(Db db)
{
    public string ServerName { get => Get("server_name") ?? "WillowWallet Server"; set => Set("server_name", value); }
    public bool OpenSignup { get => Get("open_signup") == "1"; set => Set("open_signup", value ? "1" : "0"); }
    /// <summary>0 = no limit.</summary>
    public int MaxUsers { get => int.TryParse(Get("max_users"), out int n) ? n : 0; set => Set("max_users", value.ToString()); }
    /// <summary>Paused by an admin (survives restarts).</summary>
    public bool Paused { get => Get("paused") == "1"; set => Set("paused", value ? "1" : "0"); }

    /// <summary>A random secret for this server (used to answer sign-in checks for unknown usernames consistently).</summary>
    public byte[] InstanceSecret
    {
        get
        {
            var v = Get("instance_secret");
            if (v == null) { v = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)); Set("instance_secret", v); }
            return Convert.FromBase64String(v);
        }
    }

    public string? Get(string key)
    {
        using var c = db.Open();
        return Db.Scalar(c, "SELECT value FROM settings WHERE key = $key", new { key }) as string;
    }

    public void Set(string key, string value)
    {
        using var c = db.Open();
        Db.Exec(c, "INSERT INTO settings(key, value) VALUES($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value", new { key, value });
    }
}

public sealed record ActivityEntry(long Id, DateTime At, string Kind, string? Username, string Detail);

/// <summary>A short history of what happened (sign-ups, sign-ins, admin actions), kept in the database.</summary>
public sealed class ActivityLog(Db db)
{
    public void Add(string kind, string? username, string detail)
    {
        using var c = db.Open();
        Db.Exec(c, "INSERT INTO activity(at, kind, username, detail) VALUES($at, $kind, $username, $detail)", new { at = Db.Now(), kind, username, detail });
        Db.Exec(c, "DELETE FROM activity WHERE id <= (SELECT MAX(id) FROM activity) - 5000");
    }

    public List<ActivityEntry> Recent(int count, string? username = null)
    {
        using var c = db.Open();
        return Db.Query(c,
            "SELECT id, at, kind, username, detail FROM activity WHERE ($username IS NULL OR username = $username COLLATE NOCASE) ORDER BY id DESC LIMIT $count",
            r => new ActivityEntry(r.GetInt64(0), Db.Date(r, 1)!.Value, r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4)),
            new { count, username });
    }
}

/// <summary>Whether sync is running, plus live numbers for the web panel (requests and syncs per minute, the last hour).</summary>
public sealed class ServiceState
{
    public DateTime StartedAt { get; } = DateTime.UtcNow;
    public static string Version => typeof(ServiceState).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private readonly SettingsStore _settings;
    private volatile bool _paused;
    public ServiceState(SettingsStore settings)
    {
        _settings = settings;
        _paused = settings.Paused;
    }

    public bool Running => !_paused;

    public void SetRunning(bool running)
    {
        _paused = !running;
        _settings.Paused = !running;
    }

    private readonly long[] _requests = new long[60], _syncs = new long[60], _errors = new long[60];
    private long _minute = CurrentMinute();
    private readonly object _lock = new();
    public long TotalRequests;
    public long TotalSyncs;
    public long BytesIn, BytesOut;
    private readonly ConcurrentDictionary<string, DateTime> _activeUsers = new();

    private static long CurrentMinute() => DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;

    private void Roll()
    {
        long now = CurrentMinute();
        if (now == _minute) return;
        for (long m = _minute + 1; m <= now && m <= _minute + 60; m++)
        {
            int i = (int)(m % 60);
            _requests[i] = 0; _syncs[i] = 0; _errors[i] = 0;
        }
        _minute = now;
    }

    public void CountRequest(bool error)
    {
        lock (_lock)
        {
            Roll();
            _requests[_minute % 60]++;
            if (error) _errors[_minute % 60]++;
        }
        Interlocked.Increment(ref TotalRequests);
    }

    public void CountSync(string username)
    {
        lock (_lock)
        {
            Roll();
            _syncs[_minute % 60]++;
        }
        Interlocked.Increment(ref TotalSyncs);
        _activeUsers[username] = DateTime.UtcNow;
    }

    /// <summary>Oldest minute first.</summary>
    public (long[] Requests, long[] Syncs, long[] Errors) LastHour()
    {
        lock (_lock)
        {
            Roll();
            long[] Order(long[] a) => Enumerable.Range(1, 60).Select(k => a[(_minute + k) % 60]).ToArray();
            return (Order(_requests), Order(_syncs), Order(_errors));
        }
    }

    /// <summary>People who synced in the last 15 minutes.</summary>
    public int OnlineUsers => _activeUsers.Count(kv => kv.Value > DateTime.UtcNow.AddMinutes(-15));
}

/// <summary>Slows down password guessing: 10 failures for a name or address in 15 minutes locks it for 15 minutes.</summary>
public sealed class LoginThrottle
{
    private readonly ConcurrentDictionary<string, List<DateTime>> _failures = new();

    public bool IsLocked(params string[] keys) => keys.Any(k =>
        _failures.TryGetValue(k, out var list) && Recent(list) >= 10);

    public void Fail(params string[] keys)
    {
        foreach (var k in keys)
        {
            var list = _failures.GetOrAdd(k, _ => []);
            lock (list) list.Add(DateTime.UtcNow);
        }
    }

    public void Succeed(params string[] keys)
    {
        foreach (var k in keys) _failures.TryRemove(k, out _);
    }

    private static int Recent(List<DateTime> list)
    {
        lock (list)
        {
            list.RemoveAll(t => t < DateTime.UtcNow.AddMinutes(-15));
            return list.Count;
        }
    }
}

public sealed record LogLine(DateTime At, string Level, string Category, string Message);

/// <summary>Keeps the last 2,000 log lines in memory for the web panel's Logs page.</summary>
public sealed class LogBuffer : ILoggerProvider
{
    private readonly ConcurrentQueue<LogLine> _lines = new();
    public IEnumerable<LogLine> Lines => _lines.ToArray();

    public void Add(LogLine line)
    {
        _lines.Enqueue(line);
        while (_lines.Count > 2000) _lines.TryDequeue(out _);
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void Dispose() { }

    private sealed class Logger(LogBuffer buffer, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            // Per-request noise from ASP.NET isn't useful on the Logs page
            if (category.StartsWith("Microsoft.AspNetCore.Hosting.Diagnostics") || category.StartsWith("Microsoft.AspNetCore.Routing")) return;
            string msg = formatter(state, exception);
            if (exception != null) msg += " — " + exception.Message;
            string shortCat = category.Split('.').Last();
            buffer.Add(new LogLine(DateTime.UtcNow, logLevel.ToString(), shortCat, msg));
        }
    }
}
