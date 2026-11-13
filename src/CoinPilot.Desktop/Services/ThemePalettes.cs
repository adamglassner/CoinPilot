using System.Windows;
using System.Windows.Media;

namespace CoinPilot.Services;

/// <summary>
/// Color themes. Pine is the original green (Themes/Light.xaml and Dark.xaml as written). Every other theme is built
/// from a hue: tinted neutrals for the background and sidebar, plus an accent color, in both light and dark mode.
/// The result is layered over the base palette, so status colors (warning, danger, income green) stay the same.
/// </summary>
public static class ThemePalettes
{
    public sealed record ColorTheme(string Name, double Hue, double Saturation, string Description);

    public const string Default = "Pine";

    public static readonly IReadOnlyList<ColorTheme> All =
    [
        new("Pine", 157, 55, "Deep green (the original)"),
        new("Ocean", 212, 62, "Calm blue"),
        new("Lagoon", 186, 72, "Teal"),
        new("Indigo", 234, 52, "Rich blue-violet"),
        new("Plum", 282, 38, "Purple"),
        new("Rose", 346, 58, "Raspberry pink"),
        new("Ember", 22, 80, "Warm orange"),
        new("Graphite", 215, 12, "Neutral slate"),
    ];

    public static ColorTheme Find(string? name) => All.FirstOrDefault(t => t.Name == name) ?? All[0];

    /// <summary>The swatch color shown in Settings (the light-mode accent).</summary>
    public static Color Swatch(ColorTheme t) => t.Name == Default ? Color.FromRgb(0x1F, 0x6B, 0x4F) : Accent(t, dark: false);

    private static Color Accent(ColorTheme t, bool dark) => dark
        ? Hsl(t.Hue, Math.Min(70, t.Saturation + 5), t.Saturation < 20 ? 68 : 62)
        : Hsl(t.Hue, t.Saturation, t.Saturation < 20 ? 34 : t.Hue is > 15 and < 60 ? 36 : 38);

    /// <summary>Brushes to layer over Light.xaml / Dark.xaml. Empty for Pine.</summary>
    public static ResourceDictionary Build(string name, bool dark)
    {
        var d = new ResourceDictionary();
        var t = Find(name);
        if (t.Name == Default) return d;

        double h = t.Hue, s = t.Saturation;
        double tint = Math.Min(s, 30) / 30.0; // Graphite gets less tint
        void Set(string key, Color c) { var b = new SolidColorBrush(c); b.Freeze(); d[key] = b; }

        var accent = Accent(t, dark);
        Set("Brush.Accent", accent);
        if (!dark)
        {
            Set("Brush.Bg", Hsl(h, 14 * tint, 95.5));
            Set("Brush.SurfaceAlt", Hsl(h, 22 * tint + 6, 93));
            Set("Brush.Border", Hsl(h, 12 * tint + 4, 88));
            Set("Brush.Skeleton", Hsl(h, 12 * tint + 4, 91));
            Set("Brush.Track", Hsl(h, 12 * tint + 4, 92));
            Set("Brush.Text", Hsl(h, 25 * tint + 5, 10));
            Set("Brush.TextMuted", Hsl(h, 8 * tint + 4, 36));
            Set("Brush.Sidebar", Hsl(h, 28 * tint + 8, 11));
            Set("Brush.SidebarHover", Hsl(h, 22 * tint + 6, 16));
            Set("Brush.SidebarSelected", Hsl(h, 22 * tint + 6, 20));
            Set("Brush.SidebarCard", Hsl(h, 22 * tint + 6, 16));
            Set("Brush.SidebarMuted", Hsl(h, 12 * tint + 4, 76));
            Set("Brush.Hero", Hsl(h, 28 * tint + 8, 11));
            Set("Brush.HeroMuted", Hsl(h, 12 * tint + 4, 76));
            Set("Brush.HeroTrack", Hsl(h, 20 * tint + 6, 23));
            Set("Brush.AccentText", Colors.White);
            Set("Brush.AccentSoft", Hsl(h, Math.Min(55, s), 92));
            Set("Brush.Mint", Hsl(h, Math.Min(75, s + 15), 68));
            Set("Brush.BarMuted", Hsl(h, 14 * tint + 4, 80));
            Set("Brush.ToastBg", Hsl(h, 28 * tint + 8, 11));
        }
        else
        {
            Set("Brush.Bg", Hsl(h, 16 * tint + 4, 7));
            Set("Brush.Surface", Hsl(h, 14 * tint + 4, 11));
            Set("Brush.SurfaceAlt", Hsl(h, 14 * tint + 4, 15));
            Set("Brush.Border", Hsl(h, 12 * tint + 4, 19));
            Set("Brush.Skeleton", Hsl(h, 12 * tint + 4, 17));
            Set("Brush.Track", Hsl(h, 12 * tint + 4, 17));
            Set("Brush.Text", Hsl(h, 14 * tint + 4, 92));
            Set("Brush.TextMuted", Hsl(h, 8 * tint + 4, 64));
            Set("Brush.Sidebar", Hsl(h, 22 * tint + 6, 5));
            Set("Brush.SidebarHover", Hsl(h, 16 * tint + 4, 10));
            Set("Brush.SidebarSelected", Hsl(h, 16 * tint + 4, 14));
            Set("Brush.SidebarCard", Hsl(h, 16 * tint + 4, 9));
            Set("Brush.SidebarMuted", Hsl(h, 8 * tint + 4, 70));
            Set("Brush.Hero", Hsl(h, Math.Min(60, s), 26));
            Set("Brush.HeroMuted", Hsl(h, 30 * tint + 6, 86));
            Set("Brush.HeroTrack", Hsl(h, Math.Min(60, s), 19));
            Set("Brush.AccentText", Hsl(h, 20 * tint + 6, 7));
            Set("Brush.AccentSoft", Hsl(h, 28 * tint + 8, 18));
            Set("Brush.Mint", Hsl(h, Math.Min(75, s + 15), 74));
            Set("Brush.BarMuted", Hsl(h, 10 * tint + 4, 28));
            Set("Brush.ToastBg", Hsl(h, 14 * tint + 4, 92));
            Set("Brush.ToastText", Hsl(h, 16 * tint + 4, 7));
        }
        return d;
    }

    /// <summary>HSL (h 0–360, s and l 0–100) to an opaque color.</summary>
    public static Color Hsl(double h, double s, double l)
    {
        s /= 100;
        l /= 100;
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double hp = (h % 360) / 60.0;
        double x = c * (1 - Math.Abs(hp % 2 - 1));
        (double r, double g, double b) = hp switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        double m = l - c / 2;
        byte B(double v) => (byte)Math.Clamp(Math.Round((v + m) * 255), 0, 255);
        return Color.FromRgb(B(r), B(g), B(b));
    }
}
