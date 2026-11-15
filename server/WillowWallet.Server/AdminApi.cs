using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace WillowWallet.Server;

/// <summary>Admin accounts for the web panel (separate from sync accounts). Sessions are an HttpOnly cookie.</summary>
public sealed class AdminAuth(Db db)
{
    public const string Cookie = "ww_admin";
    private const int Iterations = 310_000;

    public bool NeedsSetup()
    {
        using var c = db.Open();
        return (long)Db.Scalar(c, "SELECT COUNT(*) FROM admins")! == 0;
    }

    public void Create(string username, string password)
    {
        if (Protocol(username) is not { } name) throw new ApiProblem(400, "bad_username", "Admin names are 3 to 32 characters: letters, numbers, dots, dashes and underscores.");
        if ((password ?? "").Length < 10) throw new ApiProblem(400, "weak_password", "Use at least 10 characters for the admin password.");
        var salt = RandomNumberGenerator.GetBytes(16);
        using var c = db.Open();
        Db.Exec(c, "INSERT OR REPLACE INTO admins(username, hash, salt, iterations, created_at) VALUES($name, $hash, $salt, $it, $now)",
            new { name, hash = Secrets.HashPassword(password!, salt, Iterations), salt, it = Iterations, now = Db.Now() });
    }

    public bool Check(string username, string password)
    {
        using var c = db.Open();
        var found = Db.Query(c, "SELECT hash, salt, iterations FROM admins WHERE username = $username",
            r => (Hash: (byte[])r[0], Salt: (byte[])r[1], It: r.GetInt32(2)), new { username = (username ?? "").Trim() });
        if (found.Count == 0)
        {
            Secrets.HashPassword(password ?? "", new byte[16], Iterations); // same timing either way
            return false;
        }
        return Secrets.Equal(Secrets.HashPassword(password ?? "", found[0].Salt, found[0].It), found[0].Hash);
    }

    public string NewSession(string username)
    {
        string token = Secrets.NewToken();
        using var c = db.Open();
        Db.Exec(c, "DELETE FROM admin_sessions WHERE expires_at < $now", new { now = Db.Now() });
        Db.Exec(c, "INSERT INTO admin_sessions(token_hash, username, expires_at) VALUES($hash, $username, $exp)",
            new { hash = Secrets.TokenHash(token), username = username.Trim().ToLowerInvariant(), exp = DateTime.UtcNow.AddDays(7).ToString("O") });
        return token;
    }

    public string? SessionUser(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        using var c = db.Open();
        return Db.Scalar(c, "SELECT username FROM admin_sessions WHERE token_hash = $hash AND expires_at > $now",
            new { hash = Secrets.TokenHash(token), now = Db.Now() }) as string;
    }

    public void EndSession(string? token)
    {
        if (string.IsNullOrEmpty(token)) return;
        using var c = db.Open();
        Db.Exec(c, "DELETE FROM admin_sessions WHERE token_hash = $hash", new { hash = Secrets.TokenHash(token) });
    }

    private static string? Protocol(string? n) => WillowWallet.Sync.Protocol.NormalizeUsername(n);
}

public sealed record SetupRequest(string Username, string Password);
public sealed record AdminLoginRequest(string Username, string Password);
public sealed record AdminPasswordRequest(string CurrentPassword, string NewPassword);
public sealed record InviteRequest(string? Username, string? Note, int Uses = 1, int? Days = 14);
public sealed record SettingsRequest(string? ServerName, bool? OpenSignup, int? MaxUsers);

/// <summary>The web panel's API: /admin/api/… (cookie sign-in; changes also need the X-WillowWallet header).</summary>
public static class AdminApi
{
    public static void Map(WebApplication app)
    {
        var a = app.MapGroup("/admin/api");

        a.MapGet("/status", (AdminAuth auth, HttpContext http) =>
            new { needsSetup = auth.NeedsSetup(), signedIn = auth.SessionUser(http.Request.Cookies[AdminAuth.Cookie]), version = ServiceState.Version });

        a.MapPost("/setup", (SetupRequest r, AdminAuth auth, ActivityLog log, HttpContext http) =>
        {
            if (!auth.NeedsSetup()) throw new ApiProblem(409, "already_set_up", "An admin account already exists. Sign in instead.");
            auth.Create(r.Username, r.Password);
            log.Add("admin", null, $"Admin account \"{r.Username.Trim().ToLowerInvariant()}\" created");
            SetCookie(http, auth.NewSession(r.Username));
            return Results.Ok(new { ok = true });
        });

        a.MapPost("/login", (AdminLoginRequest r, AdminAuth auth, LoginThrottle t, ActivityLog log, HttpContext http) =>
        {
            string ip = ClientApi.Ip(http);
            if (t.IsLocked("admin:" + ip)) throw new ApiProblem(429, "too_many_attempts", "Too many tries. Wait 15 minutes and try again.");
            if (!auth.Check(r.Username, r.Password))
            {
                t.Fail("admin:" + ip);
                log.Add("admin", null, $"Failed admin sign-in for \"{r.Username}\" from {ip}");
                throw new ApiProblem(401, "bad_login", "Wrong admin name or password.");
            }
            t.Succeed("admin:" + ip);
            SetCookie(http, auth.NewSession(r.Username));
            return Results.Ok(new { ok = true });
        });

        a.MapPost("/logout", (AdminAuth auth, HttpContext http) =>
        {
            auth.EndSession(http.Request.Cookies[AdminAuth.Cookie]);
            http.Response.Cookies.Delete(AdminAuth.Cookie);
            return Results.Ok(new { ok = true });
        });

        // Everything below needs an admin sign-in (checked by the middleware in Program.cs)

        a.MapPost("/password", (AdminPasswordRequest r, AdminAuth auth, HttpContext http) =>
        {
            string admin = Admin(http);
            if (!auth.Check(admin, r.CurrentPassword)) throw new ApiProblem(403, "bad_password", "Your current password isn't right.");
            auth.Create(admin, r.NewPassword);
            return Results.Ok(new { ok = true });
        });

        a.MapGet("/overview", (ServiceState state, Accounts accounts, SyncStore sync, SettingsStore settings, ActivityLog log, Db db, ServerConfig config) =>
        {
            var users = accounts.List();
            var totals = sync.Totals();
            var (requests, syncs, errors) = state.LastHour();
            var proc = Process.GetCurrentProcess();
            long dbBytes = new[] { db.Path, db.Path + "-wal" }.Where(File.Exists).Sum(f => new FileInfo(f).Length);
            long freeBytes = -1;
            try { freeBytes = new DriveInfo(Path.GetPathRoot(config.DataFolder) ?? "/").AvailableFreeSpace; } catch { }
            return new
            {
                serverName = settings.ServerName,
                running = state.Running,
                version = ServiceState.Version,
                startedAt = state.StartedAt,
                uptimeSeconds = (long)(DateTime.UtcNow - state.StartedAt).TotalSeconds,
                runsAs = config.HowItRuns,
                os = RuntimeInformation.OSDescription,
                runtime = RuntimeInformation.FrameworkDescription,
                dataFolder = config.DataFolder,
                port = config.Port,
                httpsPort = config.HttpsPort,
                openSignup = settings.OpenSignup,
                users = users.Count,
                disabledUsers = users.Count(u => u.Disabled),
                onlineUsers = state.OnlineUsers,
                activeThisWeek = users.Count(u => u.LastSeenAt > DateTime.UtcNow.AddDays(-7)),
                devices = users.Sum(u => u.Devices),
                records = totals.Records,
                files = totals.Files,
                fileBytes = totals.FileBytes,
                databaseBytes = dbBytes,
                freeDiskBytes = freeBytes,
                memoryBytes = proc.WorkingSet64,
                totalRequests = state.TotalRequests,
                totalSyncs = state.TotalSyncs,
                requestsPerMinute = requests,
                syncsPerMinute = syncs,
                errorsPerMinute = errors,
                recentActivity = log.Recent(12),
            };
        });

        // ----- Service control -----

        a.MapPost("/service/stop", (ServiceState state, ActivityLog log, HttpContext http) =>
        {
            state.SetRunning(false);
            log.Add("service", null, $"Sync stopped by {Admin(http)}");
            return Results.Ok(new { running = false });
        });

        a.MapPost("/service/start", (ServiceState state, ActivityLog log, HttpContext http) =>
        {
            state.SetRunning(true);
            log.Add("service", null, $"Sync started by {Admin(http)}");
            return Results.Ok(new { running = true });
        });

        a.MapPost("/service/restart", (ServerControl control, ActivityLog log, HttpContext http) =>
        {
            log.Add("service", null, $"Server restart requested by {Admin(http)}");
            control.RestartSoon();
            return Results.Ok(new { restarting = true });
        });

        // ----- Users -----

        a.MapGet("/users", (Accounts accounts) => accounts.List());
        a.MapGet("/users/{id:long}", (long id, Accounts accounts, ActivityLog log) =>
        {
            var user = accounts.List().FirstOrDefault(u => u.Id == id) ?? throw new ApiProblem(404, "not_found", "No such user.");
            return new { user, devices = accounts.AllDevices(id), activity = log.Recent(30, user.Username) };
        });
        a.MapPost("/users/{id:long}/disable", (long id, Accounts accounts, ActivityLog log, HttpContext http) => UserAction(id, accounts, log, http, "turned off", () => accounts.SetDisabled(id, true)));
        a.MapPost("/users/{id:long}/enable", (long id, Accounts accounts, ActivityLog log, HttpContext http) => UserAction(id, accounts, log, http, "turned on", () => accounts.SetDisabled(id, false)));
        a.MapPost("/users/{id:long}/signout", (long id, Accounts accounts, ActivityLog log, HttpContext http) => UserAction(id, accounts, log, http, "signed out everywhere", () => accounts.SignOutEverywhere(id)));
        a.MapPost("/users/{id:long}/erase", (long id, Accounts accounts, SyncStore sync, ActivityLog log, HttpContext http) => UserAction(id, accounts, log, http, "had their synced data erased", () => sync.EraseData(id)));
        a.MapDelete("/users/{id:long}", (long id, Accounts accounts, SyncStore sync, ActivityLog log, HttpContext http) => UserAction(id, accounts, log, http, "was deleted", () => accounts.Delete(id, sync)));
        a.MapGet("/devices", (Accounts accounts) => accounts.AllDevices());
        a.MapDelete("/devices/{id}", (string id, Accounts accounts, ActivityLog log, HttpContext http) =>
        {
            accounts.RevokeDevice(id);
            log.Add("admin", null, $"{Admin(http)} signed out device {id}");
            return Results.Ok(new { ok = true });
        });

        // ----- Invites -----

        a.MapGet("/invites", (Accounts accounts) => accounts.Invites());
        a.MapPost("/invites", (InviteRequest r, Accounts accounts, ActivityLog log, HttpContext http) =>
        {
            var invite = accounts.CreateInvite(r.Username, r.Note, r.Uses, r.Days);
            log.Add("invite", invite.Username, $"{Admin(http)} created invite {invite.Code}" + (invite.Username != null ? $" for {invite.Username}" : ""));
            return invite;
        });
        a.MapDelete("/invites/{code}", (string code, Accounts accounts, ActivityLog log, HttpContext http) =>
        {
            accounts.DeleteInvite(code);
            log.Add("invite", null, $"{Admin(http)} deleted invite {code}");
            return Results.Ok(new { ok = true });
        });

        // ----- Settings, logs, backup -----

        a.MapGet("/settings", (SettingsStore s) => new { serverName = s.ServerName, openSignup = s.OpenSignup, maxUsers = s.MaxUsers });
        a.MapPut("/settings", (SettingsRequest r, SettingsStore s, ActivityLog log, HttpContext http) =>
        {
            if (r.ServerName != null)
            {
                string name = r.ServerName.Trim();
                if (name.Length is < 1 or > 60) throw new ApiProblem(400, "bad_request", "The server name can be 1 to 60 characters.");
                s.ServerName = name;
            }
            if (r.OpenSignup != null) s.OpenSignup = r.OpenSignup.Value;
            if (r.MaxUsers != null) s.MaxUsers = Math.Max(0, r.MaxUsers.Value);
            log.Add("settings", null, $"Settings changed by {Admin(http)}");
            return new { serverName = s.ServerName, openSignup = s.OpenSignup, maxUsers = s.MaxUsers };
        });

        a.MapGet("/logs", (LogBuffer logs) => logs.Lines.Reverse().Take(1000));
        a.MapGet("/activity", (ActivityLog log) => log.Recent(300));

        a.MapGet("/backup", (Db db, ActivityLog log, HttpContext http) =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "willowwallet-backups");
            Directory.CreateDirectory(folder);
            foreach (var old in Directory.GetFiles(folder)) try { File.Delete(old); } catch { }
            string file = db.BackupTo(folder);
            log.Add("admin", null, $"{Admin(http)} downloaded a database backup");
            return Results.File(file, "application/octet-stream", Path.GetFileName(file));
        });
    }

    private static IResult UserAction(long id, Accounts accounts, ActivityLog log, HttpContext http, string what, Action action)
    {
        string name = accounts.NameOf(id) ?? throw new ApiProblem(404, "not_found", "No such user.");
        action();
        log.Add("admin", name, $"{name} {what} by {Admin(http)}");
        return Results.Ok(new { ok = true });
    }

    public static string Admin(HttpContext http) => http.Items["admin"] as string ?? throw new ApiProblem(401, "unauthorized", "Sign in to the admin panel.");

    private static void SetCookie(HttpContext http, string token) =>
        http.Response.Cookies.Append(AdminAuth.Cookie, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = http.Request.IsHttps,
            MaxAge = TimeSpan.FromDays(7),
            Path = "/",
        });
}

/// <summary>Restarts the server process in the way that fits how it was started.</summary>
public sealed class ServerControl(ServerConfig config, IHostApplicationLifetime lifetime, ILogger<ServerControl> log)
{
    /// <summary>Set when a hand-started server should start a fresh copy of itself after shutting down (Program.cs).</summary>
    public static bool RelaunchAfterExit { get; private set; }

    public void RestartSoon() => _ = Task.Run(async () =>
    {
        await Task.Delay(800); // let the response go out
        log.LogWarning("Restarting ({How})", config.HowItRuns);
        if (config.InContainer || config.AsService)
        {
            // Docker's restart policy, systemd's Restart=always and the Windows service's recovery settings bring it back.
            Environment.Exit(75);
        }
        else
        {
            RelaunchAfterExit = true;
            lifetime.StopApplication();
        }
    });

    /// <summary>Called once the old copy has stopped listening.</summary>
    public static void Relaunch()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
        foreach (var arg in Environment.GetCommandLineArgs().Skip(1)) start.ArgumentList.Add(arg);
        // `dotnet WillowWalletServer.dll` reports dotnet as the process: pass the dll along
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Insert(0, typeof(ServerControl).Assembly.Location);
        Process.Start(start);
    }
}
