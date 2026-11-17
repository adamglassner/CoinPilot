using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>
/// What's new: the changelog that ships inside the app (CHANGELOG.md). Shown once after an update, and from
/// Settings → About → What's new.
/// </summary>
public sealed class WhatsNewDialog : DialogShell
{
    public WhatsNewDialog()
        : base("What's new", $"WillowWallet {App.VersionText} for Windows. The newest changes first.", allowDelete: false, saveText: "Close", width: 640)
    {
        HeaderGlyph = "";
        HideCancel();
        foreach (var (version, lines) in Releases(ReadChangelog()).Take(8))
        {
            Body.Children.Add(new TextBlock
            {
                Text = version,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, Body.Children.Count <= 1 ? 4 : 18, 0, 6),
                FontFamily = (System.Windows.Media.FontFamily)FindResource("DisplayFont"),
            });
            foreach (var line in lines) Body.Children.Add(Line(line));
        }
    }

    protected override string? TrySave() => null;

    /// <summary>The changelog built into this copy of the app.</summary>
    public static string ReadChangelog()
    {
        using var stream = typeof(WhatsNewDialog).Assembly.GetManifestResourceStream("CHANGELOG.md");
        if (stream == null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>"## 1.6.0 — 2026-11-17" sections with their lines (bullets joined with their wrapped lines).</summary>
    public static List<(string Version, List<string> Lines)> Releases(string markdown)
    {
        var result = new List<(string, List<string>)>();
        List<string>? current = null;
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            if (raw.StartsWith("## "))
            {
                current = [];
                result.Add((raw[3..].Replace(" — ", " · ").Trim(), current));
                continue;
            }
            if (current == null || raw.Trim().Length == 0) continue;
            string line = raw.TrimEnd();
            bool bullet = line.TrimStart().StartsWith("- ");
            if (!bullet && current.Count > 0 && raw.StartsWith(" ")) current[^1] += " " + line.Trim();
            else current.Add(line.Trim());
        }
        return result;
    }

    /// <summary>A bullet (indented one level for nested ones) with **bold** parts and links shown as text.</summary>
    private static UIElement Line(string line)
    {
        bool bullet = line.StartsWith("- ");
        string text = bullet ? line[2..] : line;
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1").Replace("`", "");
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(bullet ? 16 : 0, 2, 0, 4), LineHeight = 20 };
        var parts = Regex.Split(text, @"(\*\*[^*]+\*\*)");
        if (bullet) block.Inlines.Add(new Run("•  "));
        foreach (var part in parts)
        {
            if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4) block.Inlines.Add(new Bold(new Run(part[2..^2])));
            else if (part.Length > 0) block.Inlines.Add(new Run(part));
        }
        return block;
    }

    /// <summary>Shows What's new once after an update (not on a first install).</summary>
    public static void ShowAfterUpdate(Window owner)
    {
        var s = App.Settings;
        string now = App.VersionText;
        if (s.LastSeenVersion == now) return;
        bool firstRun = string.IsNullOrEmpty(s.LastSeenVersion) && CoinPilot.Data.DataStore.IsEmpty;
        s.LastSeenVersion = now;
        s.Save();
        if (firstRun) return;
        new WhatsNewDialog { Owner = owner }.ShowDialog();
    }
}
