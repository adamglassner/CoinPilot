using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>
/// A themed add/edit dialog: a floating rounded card (no Windows frame) with an icon badge, title, subtitle and close
/// button, a body of fields, and Delete / Cancel / Save buttons. The main window dims behind it.
/// Subclasses add fields with the helpers below and decide in <see cref="TrySave"/> whether the input is valid.
/// </summary>
public abstract class DialogShell : Window
{
    private const double ShadowRoom = 28, Radius = 18;

    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _errorBar = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 14), Visibility = Visibility.Collapsed };
    protected readonly StackPanel Body = new() { Margin = new Thickness(28, 4, 28, 12) };

    private readonly TextBlock _glyph = new() { FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _badgeTint = new() { CornerRadius = new CornerRadius(12) };
    private readonly Border _card = new() { CornerRadius = new CornerRadius(Radius), BorderThickness = new Thickness(1) };
    private Button? _cancel;

    /// <summary>True when the dialog closed because the person confirmed Delete.</summary>
    public bool DeleteRequested { get; private set; }

    /// <summary>The Segoe MDL2/Fluent glyph in the header badge (picked from the title unless a dialog sets it).</summary>
    protected string HeaderGlyph { get => _glyph.Text; set => _glyph.Text = value; }

    protected DialogShell(string title, string subtitle, bool allowDelete, string saveText, double width = 560)
    {
        Title = title;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/coinpilot.ico"));
        Width = width + ShadowRoom * 2;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current.MainWindow is { IsLoaded: true } owner && owner != this) Owner = owner;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13.5;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        SetResourceReference(ForegroundProperty, "Brush.Text");

        // ----- Card -----
        _card.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        _card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        _card.Margin = new Thickness(ShadowRoom);
        _card.Effect = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 8, Direction = 270, Opacity = 0.28, Color = Colors.Black };
        _card.RenderTransformOrigin = new Point(0.5, 0.5);
        _card.RenderTransform = new ScaleTransform(1, 1);
        var root = new DockPanel();

        // ----- Header: badge, title + subtitle, close -----
        var header = new DockPanel { Margin = new Thickness(28, 24, 18, 14), Background = Brushes.Transparent };
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { } };

        var badge = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 16, 0) };
        _badgeTint.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        _glyph.Style = (Style)FindResource("Icon");
        _glyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        _glyph.Text = GlyphFor(title);
        badge.Children.Add(_badgeTint);
        badge.Children.Add(_glyph);
        DockPanel.SetDock(badge, Dock.Left);
        header.Children.Add(badge);

        var close = new Button { Style = (Style)FindResource("BaseButton"), Padding = new Thickness(8), MinHeight = 32, MinWidth = 32, VerticalAlignment = VerticalAlignment.Top, ToolTip = "Close (Esc)", Focusable = false };
        AutomationProperties.SetName(close, "Close");
        close.SetResourceReference(ForegroundProperty, "Brush.TextMuted");
        var x = new TextBlock { Text = "\uE711", FontSize = 12, Style = (Style)FindResource("Icon") };
        close.Content = x;
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        titles.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = (FontFamily)FindResource("DisplayFont") });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var sub = new TextBlock { Text = subtitle, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 19 };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            titles.Children.Add(sub);
        }
        header.Children.Add(titles);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // ----- Footer -----
        var footer = new Border { Padding = new Thickness(24, 14, 24, 14), BorderThickness = new Thickness(0, 1, 0, 0), CornerRadius = new CornerRadius(0, 0, Radius - 1, Radius - 1) };
        footer.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        footer.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var footerDock = new DockPanel();
        if (allowDelete)
        {
            var delete = new Button { Style = (Style)FindResource("GhostButton") };
            var deleteLabel = new StackPanel { Orientation = Orientation.Horizontal };
            deleteLabel.Children.Add(new TextBlock { Text = "\uE74D", Style = (Style)FindResource("Icon"), FontSize = 13, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            var deleteText = new TextBlock { Text = "Delete", VerticalAlignment = VerticalAlignment.Center };
            deleteLabel.Children.Add(deleteText);
            delete.Content = deleteLabel;
            delete.SetResourceReference(ForegroundProperty, "Brush.Danger");
            delete.Click += (_, _) =>
            {
                if (deleteText.Text == "Delete")
                {
                    deleteText.Text = "Click again to delete";
                    return;
                }
                DeleteRequested = true;
                DialogResult = true;
            };
            DockPanel.SetDock(delete, Dock.Left);
            footerDock.Children.Add(delete);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 10, 0), MinWidth = 96 };
        _cancel = cancel;
        var save = new Button { Content = saveText, IsDefault = true, Style = (Style)FindResource("PrimaryButton"), MinWidth = 110 };
        save.Click += (_, _) => Save();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        footerDock.Children.Add(buttons);
        footer.Child = footerDock;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        // ----- Body (error banner first) -----
        _errorBar.SetResourceReference(Border.BorderBrushProperty, "Brush.Danger");
        _errorBar.BorderThickness = new Thickness(1);
        var errorTint = new Grid();
        var tint = new Border { CornerRadius = new CornerRadius(10), Opacity = 0.10, Margin = new Thickness(-12, -9, -12, -9) };
        tint.SetResourceReference(Border.BackgroundProperty, "Brush.Danger");
        var errorRow = new StackPanel { Orientation = Orientation.Horizontal };
        var errorIcon = new TextBlock { Text = "\uE783", Style = (Style)FindResource("Icon"), FontSize = 14, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        errorIcon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        errorRow.Children.Add(errorIcon);
        errorRow.Children.Add(_error);
        errorTint.Children.Add(tint);
        errorTint.Children.Add(errorRow);
        _errorBar.Child = errorTint;
        Body.Children.Add(_errorBar);
        // Fits small laptop screens too: header + footer + shadow take about 250px
        double room = Math.Max(240, SystemParameters.WorkArea.Height - 250);
        root.Children.Add(new ScrollViewer { Content = Body, MaxHeight = Math.Min(600, room), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });

        _card.Child = root;
        Content = _card;

        Loaded += (_, _) => { DimOwner(true); Animate(); };
        Closed += (_, _) => DimOwner(false);
    }

    /// <summary>Danger dialogs (delete, restore, turn off protection) get a red badge.</summary>
    protected void UseDangerTone(string glyph = "\uE7BA")
    {
        _glyph.Text = glyph;
        _glyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        _badgeTint.SetResourceReference(Border.BackgroundProperty, "Brush.Danger");
        _badgeTint.Opacity = 0.14;
    }

    private static string GlyphFor(string title)
    {
        string t = title.ToLowerInvariant();
        (string Word, string Glyph)[] map =
        [
            ("import", "\uE8B5"), ("transaction", "\uE8C7"), ("template", "\uE8A9"), ("budget", "\uE9D2"), ("account", "\uE825"),
            ("pay ", "\uE8C7"), ("debt", "\uE8EF"), ("bill", "\uE787"), ("goal", "\uE734"), ("tax", "\uE8A5"), ("recovery", "\uE8D7"),
            ("pin", "\uE72E"), ("protect", "\uE72E"), ("password", "\uE72E"), ("categor", "\uE8EC"), ("rename", "\uE8AC"),
            ("restore", "\uE777"), ("backup", "\uE777"),
        ];
        foreach (var (word, glyph) in map) if (t.Contains(word)) return glyph;
        return title.StartsWith("Edit", StringComparison.OrdinalIgnoreCase) ? "\uE70F" : "\uE734";
    }

    private void DimOwner(bool on)
    {
        if (Owner is MainWindow main) main.SetDimmed(on);
    }

    private void Animate()
    {
        if (SelfTest.IsRunning) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var time = TimeSpan.FromMilliseconds(190);
        _card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, time) { EasingFunction = ease });
        var scale = (ScaleTransform)_card.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, time) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, time) { EasingFunction = ease });
    }

    /// <summary>The dialog's card (without the transparent shadow margin), for screenshots.</summary>
    public FrameworkElement Card => _card;

    /// <summary>Check the input and copy it out. Return an error message, or null when it's OK to close.</summary>
    protected abstract string? TrySave();

    private void Save()
    {
        string? error = TrySave();
        if (error != null)
        {
            _error.Text = error;
            _errorBar.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            Save();
            return;
        }
        if (e.Key == Key.Escape && _cancel?.Visibility != Visibility.Visible)
        {
            e.Handled = true;
            Close();
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    // ----- Field helpers -----

    /// <summary>Adds a labelled field and returns the input so callers can keep a reference.</summary>
    protected T AddField<T>(string label, T input, string? hint = null, bool required = false) where T : UIElement
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var text = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        text.Inlines.Add(label);
        if (required)
        {
            var star = new Run(" *");
            star.SetResourceReference(TextElement.ForegroundProperty, "Brush.Danger");
            text.Inlines.Add(star);
        }
        panel.Children.Add(text);
        panel.Children.Add(input);
        if (!string.IsNullOrEmpty(hint))
        {
            var h = new TextBlock { Text = hint, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) };
            h.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            panel.Children.Add(h);
        }
        Body.Children.Add(panel);
        return input;
    }

    /// <summary>A row of pill choices. Reads back with <see cref="Selected"/>.</summary>
    protected WrapPanel Chips(IEnumerable<string> options, string? selected)
    {
        var wrap = new WrapPanel();
        SetChips(wrap, options, selected);
        return wrap;
    }

    protected void SetChips(WrapPanel wrap, IEnumerable<string> options, string? selected) =>
        SetChips(wrap, options.Select(o => (o, o)), selected);

    /// <summary>Chips whose label differs from the value they stand for (e.g. "Dining out › Coffee" for "Coffee").</summary>
    protected void SetChips(WrapPanel wrap, IEnumerable<(string Value, string Label)> options, string? selected)
    {
        wrap.Children.Clear();
        string group = Guid.NewGuid().ToString("N");
        foreach (var (value, label) in options)
        {
            wrap.Children.Add(new RadioButton
            {
                Content = label,
                Tag = value,
                GroupName = group,
                IsChecked = string.Equals(value, selected, StringComparison.OrdinalIgnoreCase),
                Style = (Style)FindResource("ChipRadio"),
            });
        }
    }

    /// <summary>The picked chip's value (its Tag), or null.</summary>
    protected static string? Selected(WrapPanel wrap) =>
        wrap.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true) is { } r ? (r.Tag as string ?? r.Content as string) : null;

    /// <summary>Category chips: subcategories show as "Parent › Sub".</summary>
    protected WrapPanel CategoryChips(IEnumerable<string> categories, string? selected)
    {
        var wrap = new WrapPanel();
        SetCategoryChips(wrap, categories, selected);
        return wrap;
    }

    protected void SetCategoryChips(WrapPanel wrap, IEnumerable<string> categories, string? selected) =>
        SetChips(wrap, categories.Select(c => (c, CoinPilot.Data.Categories.Display(c))), selected);

    /// <summary>Hides the Cancel button (for dialogs where changes apply immediately).</summary>
    protected void HideCancel()
    {
        if (_cancel != null) _cancel.Visibility = Visibility.Collapsed;
    }

    /// <summary>Hides a field added with <see cref="AddField{T}"/> (and its label).</summary>
    protected static void ShowField(UIElement input, bool visible)
    {
        if (VisualTreeHelperParent(input) is UIElement panel) panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static DependencyObject? VisualTreeHelperParent(UIElement e) => LogicalTreeHelper.GetParent(e);

    protected static CheckBox Check(string text, bool value) => new() { Content = text, IsChecked = value };

    protected static TextBox SmallBox(string text) => new() { Text = text, Width = 200, HorizontalAlignment = HorizontalAlignment.Left };

    protected static DatePicker DateBox(DateTime? value) => new() { SelectedDate = value, Width = 270, HorizontalAlignment = HorizontalAlignment.Left };

    /// <summary>Reads an amount box: blank is allowed (as 0) only when <paramref name="allowZero"/>.</summary>
    protected static bool ReadAmount(TextBox box, bool allowZero, out decimal value)
    {
        if (allowZero && string.IsNullOrWhiteSpace(box.Text)) { value = 0; return true; }
        return Money.TryParse(box.Text, out value) && (allowZero || value > 0);
    }

    protected static TextBox AmountBox(decimal? value) => new()
    {
        Text = value is decimal v && v != 0 ? Money.Plain(v) : "",
        Width = 200,
        HorizontalAlignment = HorizontalAlignment.Left,
        FontSize = 16,
    };
}
