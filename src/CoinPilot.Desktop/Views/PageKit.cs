using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CoinPilot.Data;

namespace CoinPilot.Views;

/// <summary>
/// Base for pages built from code (Accounts, Debts, Bills, Goals, Reports). <see cref="Build"/> fills <see cref="Root"/>
/// and runs again whenever saved data changes, so the page is always current.
/// </summary>
public abstract class DataPage : UserControl
{
    protected readonly StackPanel Root = new();

    protected DataPage()
    {
        Content = Root;
        Loaded += (_, _) => { DataStore.Changed += OnDataChanged; Rebuild(); };
        Unloaded += (_, _) => DataStore.Changed -= OnDataChanged;
    }

    private void OnDataChanged(object? sender, EventArgs e) => Rebuild();

    public void Rebuild()
    {
        Root.Children.Clear();
        Build();
    }

    protected abstract void Build();

    protected void Add(UIElement element) => Root.Children.Add(element);

    protected Style Res(string key) => (Style)FindResource(key);
}

/// <summary>Builders for the page pieces every money page shares: header, buttons, summary tiles, cards and pills.</summary>
public static class PageKit
{
    private static Style S(string key) => (Style)Application.Current.FindResource(key);

    /// <summary>Icon tile + title + tagline, with optional content on the right (like the month switcher).</summary>
    public static DockPanel Header(string glyph, string title, string tagline, UIElement? right = null)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
        if (right != null)
        {
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);
        }
        var tile = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 16, 0) };
        tile.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        var icon = new TextBlock { Text = glyph, Style = S("Icon"), FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        tile.Child = icon;
        dock.Children.Add(tile);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = title, Style = S("PageTitle") });
        var tag = new TextBlock { Text = tagline, Style = S("Muted"), FontSize = 14, Margin = new Thickness(0, 4, 0, 0) };
        titles.Children.Add(tag);
        dock.Children.Add(titles);
        return dock;
    }

    /// <summary>A row with explanatory text on the left and buttons on the right.</summary>
    public static DockPanel ActionBar(string? note, params UIElement[] buttons)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] is FrameworkElement fe && i < buttons.Length - 1) fe.Margin = new Thickness(0, 0, 8, 0);
            right.Children.Add(buttons[i]);
        }
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(note == null ? new Border() : new TextBlock { Text = note, Style = S("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) });
        return dock;
    }

    public static Button Primary(string glyph, string text, RoutedEventHandler onClick) => IconTextButton("PrimaryButton", glyph, text, onClick);
    public static Button Ghost(string glyph, string text, RoutedEventHandler onClick) => IconTextButton("GhostButton", glyph, text, onClick);

    private static Button IconTextButton(string style, string glyph, string text, RoutedEventHandler onClick)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, Style = S("Icon"), Margin = new Thickness(0, 0, 7, 0) });
        content.Children.Add(new TextBlock { Text = text });
        var b = new Button { Content = content, Style = S(style) };
        AutomationProperties.SetName(b, text);
        b.Click += onClick;
        return b;
    }

    /// <summary>A small text button. When it sits inside a clickable row, its click doesn't also open the row.</summary>
    public static Button Link(string text, Action onClick)
    {
        var b = new Button { Content = text, Style = S("LinkButton"), VerticalAlignment = VerticalAlignment.Center };
        b.Click += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    /// <summary>Shrinks a big number to fit its tile instead of cutting it off with "…".</summary>
    public static Viewbox FitWidth(FrameworkElement value)
    {
        var box = new Viewbox
        {
            Stretch = System.Windows.Media.Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = value.Margin,
        };
        value.Margin = new Thickness(0);
        box.Child = value;
        return box;
    }

    /// <summary>One summary tile. <paramref name="hero"/> uses the dark highlight style.</summary>
    public static Border Kpi(string label, string value, string? caption = null, bool hero = false, string? valueBrush = null)
    {
        var panel = new StackPanel();
        var l = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
        l.SetResourceReference(TextBlock.ForegroundProperty, hero ? "Brush.HeroMuted" : "Brush.TextMuted");
        panel.Children.Add(l);
        var v = new TextBlock { Text = value, Style = S("BigNumber"), TextTrimming = TextTrimming.None };
        v.SetResourceReference(TextBlock.ForegroundProperty, valueBrush ?? (hero ? "Brush.HeroText" : "Brush.Text"));
        panel.Children.Add(FitWidth(v));
        if (caption != null)
        {
            var c = new TextBlock { Text = caption, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
            c.SetResourceReference(TextBlock.ForegroundProperty, hero ? "Brush.Mint" : "Brush.TextMuted");
            panel.Children.Add(c);
        }

        var border = new Border { Child = panel, Margin = new Thickness(0, 0, 14, 0) };
        if (hero)
        {
            border.CornerRadius = new CornerRadius(14);
            border.Padding = new Thickness(20);
            border.SetResourceReference(Border.BackgroundProperty, "Brush.Hero");
        }
        else border.Style = S("Card");
        return border;
    }

    public static UniformGrid KpiRow(params Border[] tiles)
    {
        var grid = new UniformGrid { Rows = 1, Margin = new Thickness(0, 0, -14, 18) };
        foreach (var t in tiles) grid.Children.Add(t);
        return grid;
    }

    /// <summary>A white rounded card. With a title, the title sits at the top (with an optional link on the right).</summary>
    public static Border Card(UIElement content, string? title = null, UIElement? titleRight = null, Thickness? padding = null, double bottom = 18)
    {
        var card = new Border { Style = S("Card"), Margin = new Thickness(0, 0, 0, bottom) };
        if (padding is Thickness p) card.Padding = p;
        if (title == null)
        {
            card.Child = content;
            return card;
        }
        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        if (titleRight != null)
        {
            DockPanel.SetDock(titleRight, Dock.Right);
            head.Children.Add(titleRight);
        }
        head.Children.Add(new TextBlock { Text = title, Style = S("CardTitle"), VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(head);
        stack.Children.Add(content);
        card.Child = stack;
        return card;
    }

    /// <summary>A small rounded status label, colored with a theme brush (Brush.Danger, Brush.Warn, Brush.Positive, Brush.TextMuted…).</summary>
    public static Border Pill(string text, string brushKey)
    {
        var t = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        var b = new Border
        {
            Child = t,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 2, 9, 3),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.SetResourceReference(Border.BorderBrushProperty, brushKey);
        return b;
    }

    /// <summary>A clickable table row (hover highlight, divider line on top) wrapping any content.</summary>
    public static Button Row(UIElement content, string automationName, Action onClick, bool firstRow = false)
    {
        var row = new Button { Content = content, Style = S("RowButton"), ToolTip = "Edit or delete" };
        if (firstRow) row.BorderThickness = new Thickness(0);
        AutomationProperties.SetName(row, automationName);
        row.Click += (_, _) => onClick();
        return row;
    }

    /// <summary>Muted small-caps section label inside a card.</summary>
    public static TextBlock Section(string text, double top = 14) =>
        new() { Text = text.ToUpperInvariant(), Style = S("SectionLabel"), Margin = new Thickness(10, top, 0, 4) };

    /// <summary>Pill-shaped radio choices (like "Avalanche / Snowball"). Calls <paramref name="onPick"/> with the picked text.</summary>
    public static WrapPanel ChoiceChips(IEnumerable<string> options, string selected, Action<string> onPick)
    {
        var wrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        string group = Guid.NewGuid().ToString("N");
        foreach (var option in options)
        {
            var chip = new RadioButton { Content = option, GroupName = group, IsChecked = option == selected, Style = S("ChipRadio"), Margin = new Thickness(0, 0, 8, 0) };
            chip.Checked += (_, _) => onPick(option);
            wrap.Children.Add(chip);
        }
        return wrap;
    }
}
