using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WillowWallet.Sync;

/// <summary>A problem talking to the sync server, with a message that can be shown to people.</summary>
public sealed class SyncApiException(HttpStatusCode? status, string error, string message) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
    /// <summary>Machine-readable reason, e.g. "unauthorized", "paused", "invite_required", "offline".</summary>
    public string Error { get; } = error;
    public bool IsAuthProblem => Status == HttpStatusCode.Unauthorized;
}

/// <summary>Talks to a WillowWallet sync server. Thread-safe; one per server.</summary>
public sealed class SyncApiClient : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    public string? Token { get; set; }
    public Uri BaseAddress { get; }

    public SyncApiClient(string serverUrl, HttpMessageHandler? handler = null)
    {
        BaseAddress = NormalizeUrl(serverUrl);
        _http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = BaseAddress;
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    /// <summary>"myserver:8484" → "http://myserver:8484/". Throws <see cref="SyncApiException"/> when it isn't a URL.</summary>
    public static Uri NormalizeUrl(string url)
    {
        string u = (url ?? "").Trim();
        if (!u.Contains("://")) u = "http://" + u;
        if (!u.EndsWith('/')) u += "/";
        if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new SyncApiException(null, "bad_url", "That doesn't look like a server address. Try something like http://192.168.1.20:8484");
        return uri;
    }

    public Task<ServerInfo> InfoAsync(CancellationToken ct = default) => Get<ServerInfo>("api/v1/info", ct);
    public Task<PreloginResponse> PreloginAsync(string username, CancellationToken ct = default) => Post<PreloginResponse>("api/v1/auth/prelogin", new PreloginRequest(username), ct);
    public Task<SessionResponse> RegisterAsync(RegisterRequest r, CancellationToken ct = default) => Post<SessionResponse>("api/v1/auth/register", r, ct);
    public Task<SessionResponse> LoginAsync(LoginRequest r, CancellationToken ct = default) => Post<SessionResponse>("api/v1/auth/login", r, ct);
    public Task<RecoveryStartResponse> RecoveryStartAsync(RecoveryStartRequest r, CancellationToken ct = default) => Post<RecoveryStartResponse>("api/v1/auth/recover/start", r, ct);
    public Task<SessionResponse> RecoveryFinishAsync(RecoveryFinishRequest r, CancellationToken ct = default) => Post<SessionResponse>("api/v1/auth/recover/finish", r, ct);
    public Task ChangePasswordAsync(ChangePasswordRequest r, CancellationToken ct = default) => Post<JsonElement>("api/v1/auth/password", r, ct);
    public Task LogoutAsync(CancellationToken ct = default) => Post<JsonElement>("api/v1/auth/logout", new { }, ct);
    public Task<AccountResponse> AccountAsync(CancellationToken ct = default) => Get<AccountResponse>("api/v1/account", ct);
    public async Task SignOutDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        using var _ = await Send(new HttpRequestMessage(HttpMethod.Delete, "api/v1/account/devices/" + Uri.EscapeDataString(deviceId)), ct);
    }

    public Task<ChangesResponse> ChangesAsync(long since, CancellationToken ct = default) => Get<ChangesResponse>($"api/v1/sync/changes?since={since}", ct);
    public Task<PushResponse> PushAsync(PushRequest r, CancellationToken ct = default) => Post<PushResponse>("api/v1/sync/push", r, ct);

    public async Task UploadFileAsync(string name, byte[] sealedBytes, CancellationToken ct = default)
    {
        using var content = new ByteArrayContent(sealedBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        await Send(new HttpRequestMessage(HttpMethod.Put, "api/v1/files/" + EscapeName(name)) { Content = content }, ct);
    }

    /// <summary>The sealed file, or null when the server doesn't have it.</summary>
    public async Task<byte[]?> DownloadFileAsync(string name, CancellationToken ct = default)
    {
        try
        {
            using var response = await Send(new HttpRequestMessage(HttpMethod.Get, "api/v1/files/" + EscapeName(name)), ct);
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (SyncApiException e) when (e.Status == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> FileExistsAsync(string name, CancellationToken ct = default)
    {
        try
        {
            using var _ = await Send(new HttpRequestMessage(HttpMethod.Head, "api/v1/files/" + EscapeName(name)), ct);
            return true;
        }
        catch (SyncApiException e) when (e.Status == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task DeleteFileAsync(string name, CancellationToken ct = default)
    {
        try { using var _ = await Send(new HttpRequestMessage(HttpMethod.Delete, "api/v1/files/" + EscapeName(name)), ct); }
        catch (SyncApiException e) when (e.Status == HttpStatusCode.NotFound) { }
    }

    private static string EscapeName(string name) => string.Join("/", name.Split('/').Select(Uri.EscapeDataString));

    private async Task<T> Get<T>(string path, CancellationToken ct)
    {
        using var response = await Send(new HttpRequestMessage(HttpMethod.Get, path), ct);
        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private async Task<T> Post<T>(string path, object body, CancellationToken ct)
    {
        using var response = await Send(new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, body.GetType(), options: Json) }, ct);
        if (response.Content.Headers.ContentLength == 0) return default!;
        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken ct)
    {
        if (Token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SyncApiException(null, "offline", "The server didn't answer in time.");
        }
        catch (HttpRequestException e)
        {
            throw new SyncApiException(null, "offline", $"Couldn't reach the server ({e.Message}).");
        }
        if (response.IsSuccessStatusCode) return response;

        string error = "error", message = $"The server said {(int)response.StatusCode} {response.ReasonPhrase}.";
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(Json, ct);
            if (body != null) { error = body.Error; message = body.Message; }
        }
        catch { /* not JSON */ }
        response.Dispose();
        throw new SyncApiException(response.StatusCode, error, message);
    }

    public void Dispose() => _http.Dispose();
}
