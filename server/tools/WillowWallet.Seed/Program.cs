using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WillowWallet.Sync;

// Demo data: three people, a few devices, some records and a file, created exactly the way the apps would.
string url = args.Length > 0 ? args[0] : "http://localhost:8484";
string adminName = args.Length > 1 ? args[1] : "admin";
string adminPassword = args.Length > 2 ? args[2] : throw new ArgumentException("Pass the admin password.");

var cookies = new CookieContainer();
using var admin = new HttpClient(new HttpClientHandler { CookieContainer = cookies }) { BaseAddress = new Uri(url) };
admin.DefaultRequestHeaders.Add("X-WillowWallet", "admin");
(await admin.PostAsJsonAsync("/admin/api/login", new { username = adminName, password = adminPassword })).EnsureSuccessStatusCode();
(await admin.PutAsJsonAsync("/admin/api/settings", new { serverName = "Glassner Family Server" })).EnsureSuccessStatusCode();

async Task<string> Invite(string? user, string? note, int uses = 1)
{
    var r = await admin.PostAsJsonAsync("/admin/api/invites", new { username = user, note, uses, days = 14 });
    r.EnsureSuccessStatusCode();
    return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
}

var rng = new Random(7);
foreach (var (name, devices, count) in new[] { ("adam", new[] { ("ADAM-DESKTOP", "Windows", "1.1.0"), ("Pixel 8", "Android", "0.2.0") }, 240), ("sarah", new[] { ("Galaxy S24", "Android", "0.2.0") }, 120), ("mom", new[] { ("MOM-LAPTOP", "Windows", "1.1.0") }, 60) })
{
    using var api = new SyncApiClient(url);
    var (signed, _) = await SyncAccount.RegisterAsync(api, name, $"{name}-demo-password", await Invite(name, $"{char.ToUpper(name[0])}{name[1..]}'s invite"),
        new DeviceInfo(devices[0].Item1, devices[0].Item2, devices[0].Item3), 100_000);
    var changes = Enumerable.Range(0, count).Select(i =>
    {
        string id = Guid.NewGuid().ToString("N");
        string json = JsonSerializer.Serialize(new { Id = id, Payee = "Demo", Amount = -Math.Round(rng.NextDouble() * 80, 2), Date = DateTime.Today.AddDays(-i / 4) });
        return new PushChange("transactions", id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), false, SyncAccount.SealRecord(signed.DataKey, "transactions", id, json));
    }).ToList();
    await api.PushAsync(new PushRequest(changes));
    if (name == "adam")
        await api.UploadFileAsync("tax/w2-2025.pdf", SyncCrypto.SealBytes(signed.DataKey, new byte[180_000], SyncCrypto.FileAad("tax/w2-2025.pdf")));
    foreach (var d in devices.Skip(1))
        await SyncAccount.LoginAsync(new SyncApiClient(url), name, $"{name}-demo-password", new DeviceInfo(d.Item1, d.Item2, d.Item3));
    for (int i = 0; i < 5; i++) await api.ChangesAsync(0);
    Console.WriteLine($"Created {name} with {count} records");
}
await Invite(null, "For the family", 3);
var jake = new SyncApiClient(url);
await SyncAccount.RegisterAsync(jake, "jake.t", "jake-demo-password", await Invite("jake.t", null), new DeviceInfo("iPad", "Android", "0.2.0"), 100_000);
var users = await admin.GetFromJsonAsync<JsonElement>("/admin/api/users");
long jakeId = users.EnumerateArray().First(u => u.GetProperty("username").GetString() == "jake.t").GetProperty("id").GetInt64();
(await admin.PostAsync($"/admin/api/users/{jakeId}/disable", null)).EnsureSuccessStatusCode();
Console.WriteLine("Done");
