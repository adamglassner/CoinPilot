using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WillowWallet.Sync;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace WillowWallet.Server.Tests;

/// <summary>A fresh server with its own data folder and a bootstrap admin.</summary>
public sealed class TestServer : IDisposable
{
    public readonly WebApplicationFactory<Program> Factory;
    public readonly string Folder = Path.Combine(Path.GetTempPath(), "ww-test-" + Guid.NewGuid().ToString("N")[..8]);
    public const string AdminName = "admin", AdminPassword = "correct-horse-1";

    public TestServer()
    {
        Environment.SetEnvironmentVariable("WW_DATA", Folder);
        Environment.SetEnvironmentVariable("WW_ADMIN_USERNAME", AdminName);
        Environment.SetEnvironmentVariable("WW_ADMIN_PASSWORD", AdminPassword);
        Factory = new WebApplicationFactory<Program>();
        Factory.CreateClient().Dispose(); // start it now, while the environment points at our folder
    }

    public SyncApiClient Api() => new("http://localhost/", Factory.Server.CreateHandler());

    /// <summary>An HttpClient signed in to the web panel.</summary>
    public async Task<HttpClient> Admin()
    {
        var http = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        http.DefaultRequestHeaders.Add("X-WillowWallet", "admin");
        var r = await http.PostAsJsonAsync("/admin/api/login", new { username = AdminName, password = AdminPassword });
        r.EnsureSuccessStatusCode();
        return http;
    }

    public async Task<string> Invite(string? username = null, int uses = 1)
    {
        using var admin = await Admin();
        var r = await admin.PostAsJsonAsync("/admin/api/invites", new { username, uses, days = 7 });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
    }

    public void Dispose()
    {
        Factory.Dispose();
        try { Directory.Delete(Folder, true); } catch { }
    }
}

public class CryptoTests
{
    private static readonly JsonElement V = JsonDocument.Parse(File.ReadAllText("test-vectors.json")).RootElement;
    private static byte[] B(string name) => Convert.FromBase64String(V.GetProperty(name).GetString()!);
    private static string S(string name) => V.GetProperty(name).GetString()!;

    [Fact]
    public void Matches_the_reference_vectors()
    {
        var creds = SyncCrypto.DeriveFromPassword(S("password"), B("kdfSalt"), V.GetProperty("iterations").GetInt32());
        Assert.Equal(B("authSecret"), creds.AuthSecret);
        Assert.Equal(B("kek"), creds.KeyEncryptionKey);
        Assert.Equal(S("wrappedKey"), SyncCrypto.Seal(creds.KeyEncryptionKey, B("dataKey"), SyncCrypto.KeyAad, B("wrapNonce")));
        Assert.Equal(B("dataKey"), SyncCrypto.Open(creds.KeyEncryptionKey, S("wrappedKey"), SyncCrypto.KeyAad));
        Assert.Equal(S("recordPayload"), SyncCrypto.Seal(B("dataKey"), Encoding.UTF8.GetBytes(S("record")), SyncCrypto.RecordAad("transactions", "abc"), B("recordNonce")));
        Assert.Equal(S("record"), SyncAccount.OpenRecord(B("dataKey"), "transactions", "abc", S("recordPayload")));
        var rec = SyncCrypto.DeriveFromRecoveryKey(B("recoveryKeyBytes"));
        Assert.Equal(B("recoveryAuth"), rec.AuthSecret);
        Assert.Equal(B("recoveryKek"), rec.KeyEncryptionKey);
        Assert.Equal(S("recoveryKeyText"), SyncCrypto.FormatRecoveryKey(B("recoveryKeyBytes")));
        Assert.Equal(B("recoveryKeyBytes"), SyncCrypto.ParseRecoveryKey(S("recoveryKeyText").ToLowerInvariant().Replace("-", " ")));
    }

    [Fact]
    public void Sealed_data_is_bound_to_its_record()
    {
        var key = SyncCrypto.RandomBytes(32);
        string sealedRecord = SyncAccount.SealRecord(key, "budgets", "1", "{}");
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => SyncAccount.OpenRecord(key, "budgets", "2", sealedRecord));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => SyncAccount.OpenRecord(SyncCrypto.RandomBytes(32), "budgets", "1", sealedRecord));
    }
}

public class AccountTests : IDisposable
{
    private readonly TestServer _s = new();
    private static readonly DeviceInfo Pc = new("TEST-PC", "Windows", "1.1.0"), Phone = new("Pixel", "Android", "0.2.0");
    public void Dispose() => _s.Dispose();

    [Fact]
    public async Task Invite_only_by_default_then_sign_up_sign_in_and_recover()
    {
        using var api = _s.Api();
        var info = await api.InfoAsync();
        Assert.False(info.OpenSignup);
        Assert.True(info.Syncing);

        var noInvite = await Assert.ThrowsAsync<SyncApiException>(() => SyncAccount.RegisterAsync(api, "adam", "hunter2hunter2", null, Pc, 100_000));
        Assert.Equal("invite_required", noInvite.Error);

        string code = await _s.Invite("adam");
        var wrongName = await Assert.ThrowsAsync<SyncApiException>(() => SyncAccount.RegisterAsync(api, "eve", "hunter2hunter2", code, Pc, 100_000));
        Assert.Equal("bad_invite", wrongName.Error);

        var (created, recoveryKey) = await SyncAccount.RegisterAsync(api, "Adam", "hunter2hunter2", code.ToLowerInvariant(), Pc, 100_000);
        Assert.Equal("adam", created.Session.Username);
        var reuse = await Assert.ThrowsAsync<SyncApiException>(() => SyncAccount.RegisterAsync(api, "adam", "hunter2hunter2", code, Pc, 100_000));
        Assert.Equal("username_taken", reuse.Error);

        using var phone = _s.Api();
        var bad = await Assert.ThrowsAsync<SyncApiException>(() => SyncAccount.LoginAsync(phone, "adam", "wrong-password", Phone));
        Assert.Equal("bad_login", bad.Error);
        var signedIn = await SyncAccount.LoginAsync(phone, "ADAM", "hunter2hunter2", Phone);
        Assert.Equal(created.DataKey, signedIn.DataKey);

        var account = await phone.AccountAsync();
        Assert.Equal(2, account.Devices.Count);
        Assert.Single(account.Devices, d => d.IsThisDevice && d.Name == "Pixel");

        // Unknown names get a stable, fake answer (no way to tell who has an account)
        var p1 = await phone.PreloginAsync("nobody");
        var p2 = await phone.PreloginAsync("nobody");
        Assert.Equal(p1.KdfSalt, p2.KdfSalt);

        // Forgot the password: the recovery key keeps the same data key, signs out other devices
        using var newPc = _s.Api();
        var recovered = await SyncAccount.RecoverAsync(newPc, "adam", recoveryKey, "a-brand-new-password", Pc, 100_000);
        Assert.Equal(created.DataKey, recovered.DataKey);
        await Assert.ThrowsAsync<SyncApiException>(() => phone.AccountAsync());
        var wrongKey = await Assert.ThrowsAsync<SyncApiException>(() => SyncAccount.RecoverAsync(_s.Api(), "adam", SyncCrypto.NewRecoveryKey().Text, "whatever123", Pc, 100_000));
        Assert.Equal("bad_recovery", wrongKey.Error);
        var again = await SyncAccount.LoginAsync(_s.Api(), "adam", "a-brand-new-password", Pc);
        Assert.Equal(created.DataKey, again.DataKey);

        // Change password while signed in
        await SyncAccount.ChangePasswordAsync(newPc, "adam", "a-brand-new-password", "third-password!", recovered.DataKey, 100_000);
        Assert.Equal(created.DataKey, (await SyncAccount.LoginAsync(_s.Api(), "adam", "third-password!", Phone)).DataKey);
        await Assert.ThrowsAsync<SyncApiException>(() => SyncAccount.LoginAsync(_s.Api(), "adam", "a-brand-new-password", Phone));
    }

    [Fact]
    public async Task Open_signup_invite_uses_and_limits()
    {
        using var admin = await _s.Admin();
        (await admin.PutAsJsonAsync("/admin/api/settings", new { openSignup = true, maxUsers = 2 })).EnsureSuccessStatusCode();
        using var api = _s.Api();
        Assert.True((await api.InfoAsync()).OpenSignup);
        await SyncAccount.RegisterAsync(api, "one", "password1", null, Pc, 100_000);
        await SyncAccount.RegisterAsync(_s.Api(), "two", "password2", null, Pc, 100_000);
        var full = await Assert.ThrowsAsync<SyncApiException>(() => SyncAccount.RegisterAsync(_s.Api(), "three", "password3", null, Pc, 100_000));
        Assert.Equal("server_full", full.Error);

        var weak = await Assert.ThrowsAsync<SyncApiException>(() => SyncAccount.RegisterAsync(_s.Api(), "four", "short", null, Pc, 100_000));
        Assert.Equal("weak_password", weak.Error);
        var badName = await Assert.ThrowsAsync<SyncApiException>(() => SyncAccount.RegisterAsync(_s.Api(), "a b", "password4", null, Pc, 100_000));
        Assert.Equal("bad_username", badName.Error);
    }

    [Fact]
    public async Task Too_many_wrong_passwords_lock_the_name()
    {
        string code = await _s.Invite();
        await SyncAccount.RegisterAsync(_s.Api(), "target", "the-real-one", code, Pc, 100_000);
        for (int i = 0; i < 10; i++)
            await Assert.ThrowsAsync<SyncApiException>(() => SyncAccount.LoginAsync(_s.Api(), "target", "guess" + i, Pc));
        var locked = await Assert.ThrowsAsync<SyncApiException>(() => SyncAccount.LoginAsync(_s.Api(), "target", "the-real-one", Pc));
        Assert.Equal("too_many_attempts", locked.Error);
    }
}

public class SyncTests : IDisposable
{
    private readonly TestServer _s = new();
    private static readonly DeviceInfo Pc = new("PC", "Windows", "1.1.0"), Phone = new("Phone", "Android", "0.2.0");
    public void Dispose() => _s.Dispose();

    private async Task<(SyncApiClient Pc, SyncApiClient Phone, byte[] Key)> TwoDevices()
    {
        var pc = _s.Api();
        var (signed, _) = await SyncAccount.RegisterAsync(pc, "family", "password-123", await _s.Invite(), Pc, 100_000);
        var phone = _s.Api();
        await SyncAccount.LoginAsync(phone, "family", "password-123", Phone);
        return (pc, phone, signed.DataKey);
    }

    private static PushChange Rec(byte[] key, string col, string id, long modified, string json) =>
        new(col, id, modified, false, SyncAccount.SealRecord(key, col, id, json));

    [Fact]
    public async Task Changes_flow_between_devices_and_newer_edits_win()
    {
        var (pc, phone, key) = await TwoDevices();
        var push = await pc.PushAsync(new PushRequest([
            Rec(key, "transactions", "t1", 1000, """{"Id":"t1","Payee":"Kroger"}"""),
            Rec(key, "budgets", "b1", 1000, """{"Id":"b1","Category":"Groceries"}"""),
        ]));
        Assert.All(push.Results, r => Assert.Equal("ok", r.Status));
        Assert.Equal(2, push.Latest);

        var pulled = await phone.ChangesAsync(0);
        Assert.Equal(2, pulled.Changes.Count);
        Assert.Contains("Kroger", SyncAccount.OpenRecord(key, "transactions", "t1", pulled.Changes.Single(c => c.Id == "t1").Payload!));
        Assert.Empty((await phone.ChangesAsync(pulled.Latest)).Changes);

        // Phone edits later; the PC's older edit of the same record is refused as stale
        Assert.Equal("ok", (await phone.PushAsync(new PushRequest([Rec(key, "transactions", "t1", 3000, """{"Id":"t1","Payee":"Aldi"}""")]))).Results[0].Status);
        var stale = await pc.PushAsync(new PushRequest([Rec(key, "transactions", "t1", 2000, """{"Id":"t1","Payee":"Old"}""")]));
        Assert.Equal("stale", stale.Results[0].Status);
        var latest = await pc.ChangesAsync(2);
        Assert.Contains("Aldi", SyncAccount.OpenRecord(key, "transactions", "t1", latest.Changes.Single().Payload!));

        // Deletes travel as tombstones
        await pc.PushAsync(new PushRequest([new PushChange("budgets", "b1", 4000, true, null)]));
        var del = await phone.ChangesAsync(latest.Latest);
        Assert.True(del.Changes.Single(c => c.Id == "b1").Deleted);
        Assert.Null(del.Changes.Single(c => c.Id == "b1").Payload);

        // Bad input is refused
        var bad = await Assert.ThrowsAsync<SyncApiException>(() => pc.PushAsync(new PushRequest([new PushChange("transactions", "x", 1, false, "plain text")])));
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        await Assert.ThrowsAsync<SyncApiException>(() => pc.PushAsync(new PushRequest([new PushChange("../etc", "x", 1, true, null)])));
    }

    [Fact]
    public async Task Paging_through_many_changes()
    {
        var (pc, phone, key) = await TwoDevices();
        for (int batch = 0; batch < 3; batch++)
            await pc.PushAsync(new PushRequest(Enumerable.Range(0, 500).Select(i => Rec(key, "transactions", $"t{batch}-{i}", 1, "{}")).ToList()));
        var all = new List<Change>();
        long since = 0;
        while (true)
        {
            var page = await phone.ChangesAsync(since);
            all.AddRange(page.Changes);
            since = page.Changes.Count > 0 ? page.Changes[^1].Seq : page.Latest;
            if (!page.More) break;
        }
        Assert.Equal(1500, all.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public async Task Files_upload_download_and_stay_private()
    {
        var (pc, phone, key) = await TwoDevices();
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.7 W-2 for 2025");
        var sealedFile = SyncCrypto.SealBytes(key, bytes, SyncCrypto.FileAad("tax/abc.pdf"));
        await pc.UploadFileAsync("tax/abc.pdf", sealedFile);
        Assert.True(await phone.FileExistsAsync("tax/abc.pdf"));
        var back = await phone.DownloadFileAsync("tax/abc.pdf");
        Assert.Equal(bytes, SyncCrypto.OpenBytes(key, back!, SyncCrypto.FileAad("tax/abc.pdf")));
        Assert.Null(await phone.DownloadFileAsync("tax/missing.pdf"));

        // Another account can't see it
        var other = _s.Api();
        await SyncAccount.RegisterAsync(other, "stranger", "password-999", await _s.Invite(), Pc, 100_000);
        Assert.Null(await other.DownloadFileAsync("tax/abc.pdf"));

        await pc.DeleteFileAsync("tax/abc.pdf");
        Assert.False(await phone.FileExistsAsync("tax/abc.pdf"));
    }

    [Fact]
    public async Task Stopping_sync_from_the_panel()
    {
        var (pc, _, key) = await TwoDevices();
        using var admin = await _s.Admin();
        (await admin.PostAsync("/admin/api/service/stop", null)).EnsureSuccessStatusCode();
        Assert.False((await pc.InfoAsync()).Syncing);
        var paused = await Assert.ThrowsAsync<SyncApiException>(() => pc.ChangesAsync(0));
        Assert.Equal("paused", paused.Error);
        (await admin.PostAsync("/admin/api/service/start", null)).EnsureSuccessStatusCode();
        await pc.PushAsync(new PushRequest([Rec(key, "transactions", "after", 1, "{}")]));
    }
}

public class AdminTests : IDisposable
{
    private readonly TestServer _s = new();
    public void Dispose() => _s.Dispose();

    [Fact]
    public async Task Panel_needs_sign_in_and_the_header()
    {
        var anon = _s.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/admin/api/overview")).StatusCode);
        var status = await anon.GetFromJsonAsync<JsonElement>("/admin/api/status");
        Assert.False(status.GetProperty("needsSetup").GetBoolean());
        // Setup can't be used once there's an admin
        var setup = new HttpRequestMessage(HttpMethod.Post, "/admin/api/setup") { Content = JsonContent.Create(new { username = "evil", password = "0123456789x" }) };
        setup.Headers.Add("X-WillowWallet", "admin");
        Assert.Equal(HttpStatusCode.Conflict, (await anon.SendAsync(setup)).StatusCode);
        // Wrong password, and posts without the header
        var bad = new HttpRequestMessage(HttpMethod.Post, "/admin/api/login") { Content = JsonContent.Create(new { username = "admin", password = "nope" }) };
        bad.Headers.Add("X-WillowWallet", "admin");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(bad)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/admin/api/login", new { username = "admin", password = TestServer.AdminPassword })).StatusCode);

        // The web panel itself is served
        var page = await anon.GetStringAsync("/");
        Assert.Contains("WillowWallet Server", page);
        Assert.Contains("ICONS", await anon.GetStringAsync("/icons.js"));
    }

    [Fact]
    public async Task Overview_users_invites_logs_and_backup()
    {
        var api = _s.Api();
        await SyncAccount.RegisterAsync(api, "mom", "password-mom", await _s.Invite("mom"), new DeviceInfo("Pixel", "Android", "0.2.0"), 100_000);
        using var admin = await _s.Admin();

        var o = await admin.GetFromJsonAsync<JsonElement>("/admin/api/overview");
        Assert.Equal(1, o.GetProperty("users").GetInt32());
        Assert.True(o.GetProperty("running").GetBoolean());
        Assert.Equal(60, o.GetProperty("requestsPerMinute").GetArrayLength());

        var users = await admin.GetFromJsonAsync<JsonElement>("/admin/api/users");
        long id = users[0].GetProperty("id").GetInt64();
        var detail = await admin.GetFromJsonAsync<JsonElement>($"/admin/api/users/{id}");
        Assert.Equal("Pixel", detail.GetProperty("devices")[0].GetProperty("name").GetString());

        (await admin.PostAsync($"/admin/api/users/{id}/disable", null)).EnsureSuccessStatusCode();
        var off = await Assert.ThrowsAsync<SyncApiException>(() => SyncAccount.LoginAsync(_s.Api(), "mom", "password-mom", new DeviceInfo("PC", "Windows", "1")));
        Assert.Equal("disabled", off.Error);
        await Assert.ThrowsAsync<SyncApiException>(() => api.AccountAsync()); // signed out
        (await admin.PostAsync($"/admin/api/users/{id}/enable", null)).EnsureSuccessStatusCode();
        await SyncAccount.LoginAsync(_s.Api(), "mom", "password-mom", new DeviceInfo("PC", "Windows", "1"));

        var invites = await admin.GetFromJsonAsync<JsonElement>("/admin/api/invites");
        Assert.Equal("mom", invites[0].GetProperty("usedBy").GetString());

        Assert.True((await admin.GetFromJsonAsync<JsonElement>("/admin/api/activity")).GetArrayLength() >= 3);
        Assert.True((await admin.GetFromJsonAsync<JsonElement>("/admin/api/logs")).GetArrayLength() >= 1);
        var backup = await admin.GetAsync("/admin/api/backup");
        backup.EnsureSuccessStatusCode();
        Assert.StartsWith("SQLite format 3", Encoding.ASCII.GetString((await backup.Content.ReadAsByteArrayAsync())[..15]));

        (await admin.DeleteAsync($"/admin/api/users/{id}")).EnsureSuccessStatusCode();
        Assert.Equal(0, (await admin.GetFromJsonAsync<JsonElement>("/admin/api/users")).GetArrayLength());
    }

    [Fact]
    public async Task Admin_never_sees_plain_data()
    {
        var api = _s.Api();
        var (signed, _) = await SyncAccount.RegisterAsync(api, "private", "password-xyz", await _s.Invite(), new DeviceInfo("PC", "Windows", "1"), 100_000);
        await api.PushAsync(new PushRequest([new PushChange("transactions", "t", 1, false, SyncAccount.SealRecord(signed.DataKey, "transactions", "t", """{"Payee":"Secret Clinic"}"""))]));
        using var admin = await _s.Admin();
        var backup = await (await admin.GetAsync("/admin/api/backup")).Content.ReadAsByteArrayAsync();
        Assert.DoesNotContain("Secret Clinic", Encoding.UTF8.GetString(backup));
    }
}
