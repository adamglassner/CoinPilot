using System.IO;
using System.Text.Json;

namespace CoinPilot.Services;

public static class AppTheme
{
    public const string System = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";
}

/// <summary>Per-user preferences saved to %AppData%\CoinPilot\settings.json.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = AppTheme.System;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CoinPilot", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // Corrupt or unreadable settings fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Settings are a convenience; never crash over them.
        }
    }
}
