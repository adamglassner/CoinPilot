namespace WillowWallet.Server;

/// <summary>
/// Settings that need a restart, from environment variables (WW_*) or appsettings.json ("WillowWallet" section):
/// WW_DATA (data folder), WW_PORT (default 8484), WW_HTTPS_PORT + WW_HTTPS_CERT + WW_HTTPS_CERT_PASSWORD (optional),
/// WW_ADMIN_USERNAME + WW_ADMIN_PASSWORD (create the first admin without the setup page).
/// </summary>
public sealed class ServerConfig
{
    public string DataFolder { get; init; } = "";
    public int Port { get; init; } = 8484;
    public int? HttpsPort { get; init; }
    public string? HttpsCertificate { get; init; }
    public string? HttpsCertificatePassword { get; init; }
    public string? BootstrapAdmin { get; init; }
    public string? BootstrapPassword { get; init; }
    public bool InContainer { get; init; }
    public bool AsService { get; init; }

    public static ServerConfig From(IConfiguration c)
    {
        string? Get(string env, string key) => Environment.GetEnvironmentVariable(env) is { Length: > 0 } v ? v : c[$"WillowWallet:{key}"];
        bool container = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
        string data = Get("WW_DATA", "DataFolder") ?? (container ? "/data" : System.IO.Path.Combine(AppContext.BaseDirectory, "data"));
        return new ServerConfig
        {
            DataFolder = System.IO.Path.IsPathRooted(data) ? data : System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, data)),
            Port = int.TryParse(Get("WW_PORT", "Port"), out int p) ? p : 8484,
            HttpsPort = int.TryParse(Get("WW_HTTPS_PORT", "HttpsPort"), out int hp) ? hp : null,
            HttpsCertificate = Get("WW_HTTPS_CERT", "HttpsCertificate"),
            HttpsCertificatePassword = Get("WW_HTTPS_CERT_PASSWORD", "HttpsCertificatePassword"),
            BootstrapAdmin = Get("WW_ADMIN_USERNAME", "AdminUsername"),
            BootstrapPassword = Get("WW_ADMIN_PASSWORD", "AdminPassword"),
            InContainer = container,
            AsService = Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService()
                        || Microsoft.Extensions.Hosting.Systemd.SystemdHelpers.IsSystemdService(),
        };
    }

    public string HowItRuns => InContainer ? "Docker" : AsService ? (OperatingSystem.IsWindows() ? "Windows service" : "systemd service") : "Standalone";
}
