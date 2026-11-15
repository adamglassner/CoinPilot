using System.Text.Json;
using System.Text.Json.Serialization;
using WillowWallet.Server;
using WillowWallet.Sync;

// WillowWallet sync server: the API the apps sync with (/api/v1) and a web panel to run it (/).

// `--healthcheck`: used by Docker's HEALTHCHECK (the image has no curl). Exit 0 when the server answers.
if (args.Contains("--healthcheck"))
{
    int port = int.TryParse(Environment.GetEnvironmentVariable("WW_PORT"), out int hp) ? hp : 8484;
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        return (await http.GetAsync($"http://127.0.0.1:{port}/api/v1/info")).IsSuccessStatusCode ? 0 : 1;
    }
    catch { return 1; }
}

// Find wwwroot next to the program wherever it's started from (unless a host, like the test host, sets its own)
bool rootGiven = args.Any(a => a.StartsWith("--contentRoot", StringComparison.OrdinalIgnoreCase)) || Environment.GetEnvironmentVariable("ASPNETCORE_CONTENTROOT") != null;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = rootGiven ? null : AppContext.BaseDirectory });
builder.Host.UseWindowsService(o => o.ServiceName = "WillowWalletServer");
builder.Host.UseSystemd();

var config = ServerConfig.From(builder.Configuration);
Directory.CreateDirectory(config.DataFolder);

builder.WebHost.ConfigureKestrel(k =>
{
    k.ListenAnyIP(config.Port);
    if (config.HttpsPort is int hp && !string.IsNullOrEmpty(config.HttpsCertificate))
        k.ListenAnyIP(hp, l => l.UseHttps(config.HttpsCertificate, config.HttpsCertificatePassword));
    k.Limits.MaxRequestBodySize = Protocol.MaxFileBytes + 1024 * 1024;
    k.AddServerHeader = false;
});

var logBuffer = new LogBuffer();
builder.Logging.AddProvider(logBuffer);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(logBuffer);
builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<ActivityLog>();
builder.Services.AddSingleton<ServiceState>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton<Accounts>();
builder.Services.AddSingleton<SyncStore>();
builder.Services.AddSingleton<AdminAuth>();
builder.Services.AddSingleton<ServerControl>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();
var log = app.Services.GetRequiredService<ILogger<Program>>();

// Errors come back as { error, message } that the apps and the panel can show
app.Use(async (http, next) =>
{
    var state = http.RequestServices.GetRequiredService<ServiceState>();
    bool isApi = http.Request.Path.StartsWithSegments("/api");
    try
    {
        await next(http);
        if (isApi) state.CountRequest(http.Response.StatusCode >= 500);
    }
    catch (ApiProblem p)
    {
        if (isApi) state.CountRequest(false);
        await Problem(http, p.Status, p.Error, p.Message);
    }
    catch (BadHttpRequestException e)
    {
        if (isApi) state.CountRequest(false);
        await Problem(http, e.StatusCode, "bad_request", "The request couldn't be read.");
    }
    catch (Exception e) when (!http.Response.HasStarted)
    {
        if (isApi) state.CountRequest(true);
        log.LogError(e, "Error handling {Method} {Path}", http.Request.Method, http.Request.Path);
        await Problem(http, 500, "server_error", "Something went wrong on the server. The server's Logs page has details.");
    }
});

// Sync API: paused check and bearer sign-in
app.Use(async (http, next) =>
{
    var path = http.Request.Path;
    if (path.StartsWithSegments("/api/v1") && !path.StartsWithSegments("/api/v1/info"))
    {
        if (!http.RequestServices.GetRequiredService<ServiceState>().Running)
            throw new ApiProblem(503, "paused", "Syncing is stopped on this server right now. Your data is safe on this device; it will sync when the server is started again.");
        string? auth = http.Request.Headers.Authorization;
        if (auth != null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var user = http.RequestServices.GetRequiredService<Accounts>().Authenticate(auth[7..].Trim(), ClientApi.Ip(http));
            if (user != null) http.Items["user"] = user;
        }
    }
    await next(http);
});

// Web panel API: admin cookie, plus a custom header on changes (blocks cross-site form posts)
app.Use(async (http, next) =>
{
    var path = http.Request.Path;
    if (path.StartsWithSegments("/admin/api"))
    {
        bool open = path.StartsWithSegments("/admin/api/status") || path.StartsWithSegments("/admin/api/setup") || path.StartsWithSegments("/admin/api/login");
        if (!HttpMethods.IsGet(http.Request.Method) && http.Request.Headers["X-WillowWallet"] != "admin")
            throw new ApiProblem(400, "bad_request", "Missing X-WillowWallet header.");
        string? admin = http.RequestServices.GetRequiredService<AdminAuth>().SessionUser(http.Request.Cookies[AdminAuth.Cookie]);
        if (admin != null) http.Items["admin"] = admin;
        else if (!open) throw new ApiProblem(401, "unauthorized", "Sign in to the admin panel.");
    }
    await next(http);
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
});

ClientApi.Map(app);
AdminApi.Map(app);
app.MapFallback(async http =>
{
    if (http.Request.Path.StartsWithSegments("/api") || http.Request.Path.StartsWithSegments("/admin/api"))
        await Problem(http, 404, "not_found", "No such endpoint.");
    else
        await http.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "index.html"));
});

// First admin from WW_ADMIN_USERNAME / WW_ADMIN_PASSWORD (otherwise the web panel asks)
var admins = app.Services.GetRequiredService<AdminAuth>();
if (admins.NeedsSetup() && !string.IsNullOrEmpty(config.BootstrapAdmin) && !string.IsNullOrEmpty(config.BootstrapPassword))
{
    admins.Create(config.BootstrapAdmin, config.BootstrapPassword);
    log.LogInformation("Created admin account {Admin} from the environment", config.BootstrapAdmin);
}

app.Lifetime.ApplicationStarted.Register(() =>
{
    log.LogInformation("WillowWallet Server {Version} ({How}) — data in {Folder}", ServiceState.Version, config.HowItRuns, config.DataFolder);
    log.LogInformation("Web panel and sync: http://localhost:{Port}/{Https}", config.Port, config.HttpsPort is int p ? $" and https on port {p}" : "");
    if (admins.NeedsSetup()) log.LogWarning("No admin yet: open the web panel to create one");
});

await app.RunAsync();

if (ServerControl.RelaunchAfterExit) ServerControl.Relaunch();
return 0;

static Task Problem(HttpContext http, int status, string error, string message)
{
    if (http.Response.HasStarted) return Task.CompletedTask;
    http.Response.Clear();
    http.Response.StatusCode = status;
    return http.Response.WriteAsJsonAsync(new ErrorResponse(error, message));
}

public partial class Program { }
