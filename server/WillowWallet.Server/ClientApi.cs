using WillowWallet.Sync;

namespace WillowWallet.Server;

/// <summary>The API the WillowWallet apps use: /api/v1/… (see server/PROTOCOL.md).</summary>
public static class ClientApi
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapGet("/info", (SettingsStore s, ServiceState state) =>
            new ServerInfo(s.ServerName, ServiceState.Version, s.OpenSignup, state.Running, Protocol.Version));

        // ----- Accounts -----

        api.MapPost("/auth/prelogin", (PreloginRequest r, Accounts a) => a.Prelogin(r.Username));

        api.MapPost("/auth/register", (RegisterRequest r, Accounts a, LoginThrottle t, HttpContext http) =>
        {
            string ip = Ip(http);
            if (t.IsLocked("signup:" + ip)) throw TooMany();
            try { return a.Register(r, ip); }
            catch (ApiProblem p) when (p.Error is "bad_invite" or "invite_required") { t.Fail("signup:" + ip); throw; }
        });

        api.MapPost("/auth/login", (LoginRequest r, Accounts a, LoginThrottle t, HttpContext http) =>
        {
            string ip = Ip(http), user = "user:" + (r.Username ?? "").Trim().ToLowerInvariant();
            if (t.IsLocked(user, "ip:" + ip)) throw TooMany();
            try
            {
                var session = a.Login(r, ip);
                t.Succeed(user);
                return session;
            }
            catch (ApiProblem p) when (p.Status == 401) { t.Fail(user, "ip:" + ip); throw; }
        });

        api.MapPost("/auth/recover/start", (RecoveryStartRequest r, Accounts a, LoginThrottle t, HttpContext http) =>
        {
            string ip = Ip(http), user = "recover:" + (r.Username ?? "").Trim().ToLowerInvariant();
            if (t.IsLocked(user, "ip:" + ip)) throw TooMany();
            try { return a.RecoveryStart(r); }
            catch (ApiProblem p) when (p.Status == 401) { t.Fail(user, "ip:" + ip); throw; }
        });

        api.MapPost("/auth/recover/finish", (RecoveryFinishRequest r, Accounts a, LoginThrottle t, HttpContext http) =>
        {
            string ip = Ip(http), user = "recover:" + (r.Username ?? "").Trim().ToLowerInvariant();
            if (t.IsLocked(user, "ip:" + ip)) throw TooMany();
            try { return a.RecoveryFinish(r, ip); }
            catch (ApiProblem p) when (p.Status == 401) { t.Fail(user, "ip:" + ip); throw; }
        });

        api.MapPost("/auth/password", (ChangePasswordRequest r, Accounts a, HttpContext http) =>
        {
            a.ChangePassword(User(http), r);
            return Results.Ok(new { ok = true });
        });

        api.MapPost("/auth/logout", (Accounts a, HttpContext http) =>
        {
            a.Logout(User(http));
            return Results.Ok(new { ok = true });
        });

        api.MapGet("/account", (Accounts a, HttpContext http) => a.Account(User(http)));

        api.MapDelete("/account/devices/{id}", (string id, Accounts a, HttpContext http) =>
            a.SignOutDevice(User(http), id) ? Results.Ok(new { ok = true }) : throw new ApiProblem(404, "not_found", "No such device."));

        // ----- Sync -----

        api.MapGet("/sync/changes", (long? since, SyncStore sync, ServiceState state, HttpContext http) =>
        {
            var u = User(http);
            state.CountSync(u.Username);
            return sync.Changes(u.UserId, since ?? 0);
        });

        api.MapPost("/sync/push", (PushRequest r, SyncStore sync, ServiceState state, HttpContext http) =>
        {
            var u = User(http);
            state.CountSync(u.Username);
            return sync.Push(u.UserId, r);
        });

        // ----- Files -----

        api.MapPut("/files/{**name}", async (string name, SyncStore sync, HttpContext http) =>
        {
            var u = User(http);
            await sync.SaveFile(u.UserId, Uri.UnescapeDataString(name), http.Request.Body, http.RequestAborted);
            return Results.Ok(new { ok = true });
        });

        api.MapMethods("/files/{**name}", ["GET", "HEAD"], (string name, SyncStore sync, HttpContext http) =>
        {
            var u = User(http);
            string? path = sync.FileFor(u.UserId, Uri.UnescapeDataString(name));
            if (path == null) throw new ApiProblem(404, "not_found", "No such file.");
            return Results.File(path, "application/octet-stream");
        });

        api.MapDelete("/files/{**name}", (string name, SyncStore sync, HttpContext http) =>
        {
            var u = User(http);
            return sync.DeleteFile(u.UserId, Uri.UnescapeDataString(name)) ? Results.Ok(new { ok = true }) : throw new ApiProblem(404, "not_found", "No such file.");
        });
    }

    /// <summary>The signed-in user (set by the auth middleware), or a 401.</summary>
    private static SessionUser User(HttpContext http) =>
        http.Items["user"] as SessionUser ?? throw new ApiProblem(401, "unauthorized", "You're signed out. Sign in again to keep syncing.");

    public static string Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static ApiProblem TooMany() => new(429, "too_many_attempts", "Too many tries. Wait 15 minutes and try again.");
}
