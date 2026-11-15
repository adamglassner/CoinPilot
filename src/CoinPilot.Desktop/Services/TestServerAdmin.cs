using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace CoinPilot.Services;

/// <summary>Talks to a test server's web panel API (invites, stop/start) for the self-test and screenshots.</summary>
public static class TestServerAdmin
{
    private static async Task<HttpClient> SignIn(string url, string password)
    {
        var http = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(url) };
        http.DefaultRequestHeaders.Add("X-WillowWallet", "admin");
        (await http.PostAsJsonAsync("/admin/api/login", new { username = "admin", password })).EnsureSuccessStatusCode();
        return http;
    }

    public static async Task<string> Invite(string url, string password)
    {
        using var http = await SignIn(url, password);
        var r = await http.PostAsJsonAsync("/admin/api/invites", new { uses = 1, days = 1 });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
    }

    public static async Task Post(string url, string password, string path)
    {
        using var http = await SignIn(url, password);
        (await http.PostAsync(path, null)).EnsureSuccessStatusCode();
    }
}
