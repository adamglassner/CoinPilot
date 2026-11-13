using System.Windows;
using System.Windows.Controls;

namespace CoinPilot.Views;

/// <summary>Small builders for the bits of UI that pages create from code (rows, bars, labels). All colors follow the theme.</summary>
public static class Ui
{
    public static TextBlock Text(string text, bool muted = false, double size = 13.5, FontWeight? weight = null, string? brushKey = null)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, brushKey ?? (muted ? "Brush.TextMuted" : "Brush.Text"));
        return tb;
    }

    /// <summary>A rounded track with a filled part, sized with star columns so it stretches with its container.</summary>
    public static Grid ProgressBar(double fraction, string fillKey, string trackKey = "Brush.Track", double height = 10)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var grid = new Grid { Height = height };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(fraction, 0.0001), GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - fraction, 0.0001), GridUnitType.Star) });

        var track = new Border { CornerRadius = new CornerRadius(height / 2) };
        track.SetResourceReference(Border.BackgroundProperty, trackKey);
        Grid.SetColumnSpan(track, 2);
        grid.Children.Add(track);

        if (fraction > 0)
        {
            var fill = new Border { CornerRadius = new CornerRadius(height / 2), MinWidth = height };
            fill.SetResourceReference(Border.BackgroundProperty, fillKey);
            grid.Children.Add(fill);
        }
        return grid;
    }

    /// <summary>A soft rounded label, used for categories.</summary>
    public static Border Chip(string text)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 3, 10, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = Text(text, size: 12.5),
        };
        chip.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        return chip;
    }

    /// <summary>A grid whose columns use the given star widths. Used so header and rows line up.</summary>
    public static Grid Columns(params double[] stars)
    {
        var grid = new Grid();
        foreach (var s in stars) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(s, GridUnitType.Star) });
        return grid;
    }

    public static T At<T>(this T element, int column, Thickness? margin = null) where T : FrameworkElement
    {
        Grid.SetColumn(element, column);
        if (margin is Thickness m) element.Margin = m;
        return element;
    }

    /// <summary>Centered "nothing here" message with an optional button.</summary>
    public static StackPanel EmptyState(string glyph, string title, string message, string? buttonText = null, RoutedEventHandler? onClick = null)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(20, 36, 20, 36) };
        var icon = new TextBlock
        {
            Text = glyph,
            FontSize = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            Style = (Style)Application.Current.FindResource("Icon"),
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        panel.Children.Add(icon);

        var t = Text(title, size: 16, weight: FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 10, 0, 4);
        panel.Children.Add(t);

        var m = Text(message, muted: true);
        m.HorizontalAlignment = HorizontalAlignment.Center;
        m.TextAlignment = TextAlignment.Center;
        m.TextWrapping = TextWrapping.Wrap;
        m.TextTrimming = TextTrimming.None;
        m.MaxWidth = 420;
        panel.Children.Add(m);

        if (buttonText != null && onClick != null)
        {
            var b = new Button
            {
                Content = buttonText,
                Style = (Style)Application.Current.FindResource("PrimaryButton"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 16, 0, 0),
            };
            b.Click += onClick;
            panel.Children.Add(b);
        }
        return panel;
    }
}
