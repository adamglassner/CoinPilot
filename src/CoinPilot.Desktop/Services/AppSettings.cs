using System.IO;
using System.Text.Json;

namespace CoinPilot.Services;

public static class AppTheme
{
    public const string System = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";
}

/// <summary>Per-user preferences saved to %AppData%\WillowWallet\settings.json.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = AppTheme.System;
    /// <summary>Color theme name (see ThemePalettes): Pine, Ocean, Lagoon, Indigo, Plum, Rose, Ember, Graphite.</summary>
    public string ColorTheme { get; set; } = "Pine";

    /// <summary>Keep WillowWallet in the notification area for bill reminders, and start it when you sign in to Windows.</summary>
    public bool RunInBackground { get; set; }

    // Window position and size, restored on the next start.
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>Bill reminders already shown, as "billId|yyyy-MM-dd" (so each due date is announced once a day).</summary>
    public List<string> RemindersShown { get; set; } = [];

    private static string FilePath => Path.Combine(AppPaths.Root, "settings.json");

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
        if (SelfTest.IsRunning) return; // the self-test never touches real settings
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
