using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>
/// A themed add/edit dialog: title, a body of fields, and Delete / Cancel / Save buttons.
/// Subclasses add fields with the helpers below and decide in <see cref="TrySave"/> whether the input is valid.
/// </summary>
public abstract class DialogShell : Window
{
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed };
    protected readonly StackPanel Body = new() { Margin = new Thickness(24, 6, 24, 10) };

    private Button? _cancel;

    /// <summary>True when the dialog closed because the person confirmed Delete.</summary>
    public bool DeleteRequested { get; private set; }

    protected DialogShell(string title, string subtitle, bool allowDelete, string saveText, double width = 560)
    {
        Title = title;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/coinpilot.ico"));
        Width = width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current.MainWindow is { IsLoaded: true } owner && owner != this) Owner = owner;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 13.5;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "Brush.Bg");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        SourceInitialized += (_, _) => SystemTheme.ApplyTitleBar(this, App.IsDark);

        var root = new DockPanel();

        var header = new StackPanel { Margin = new Thickness(24, 20, 24, 10) };
        header.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold, FontFamily = (System.Windows.Media.FontFamily)FindResource("DisplayFont") });
        var sub = new TextBlock { Text = subtitle, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        header.Children.Add(sub);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new Border { Padding = new Thickness(24, 12, 24, 12), BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        footer.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var footerDock = new DockPanel();
        if (allowDelete)
        {
            var delete = new Button { Content = "Delete", Style = (Style)FindResource("GhostButton") };
            delete.SetResourceReference(ForegroundProperty, "Brush.Danger");
            delete.Click += (_, _) =>
            {
                if (delete.Content as string == "Delete")
                {
                    delete.Content = "Click again to delete";
                    return;
                }
                DeleteRequested = true;
                DialogResult = true;
            };
            DockPanel.SetDock(delete, Dock.Left);
            footerDock.Children.Add(delete);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 8, 0) };
        _cancel = cancel;
        var save = new Button { Content = saveText, IsDefault = true, Style = (Style)FindResource("PrimaryButton") };
        save.Click += (_, _) => Save();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        footerDock.Children.Add(buttons);
        footer.Child = footerDock;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        _error.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        Body.Children.Add(_error);
        root.Children.Add(new ScrollViewer { Content = Body, MaxHeight = 620, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    /// <summary>Check the input and copy it out. Return an error message, or null when it's OK to close.</summary>
    protected abstract string? TrySave();

    private void Save()
    {
        string? error = TrySave();
        if (error != null)
        {
            _error.Text = error;
            _error.Visibility = Visibility.Visible;
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

    protected static DatePicker DateBox(DateTime? value) => new() { SelectedDate = value, Width = 200, HorizontalAlignment = HorizontalAlignment.Left };

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
