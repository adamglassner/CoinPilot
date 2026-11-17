using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>The calculator that flies out beside the sidebar (the Calculator button, or Ctrl+K). Type on the keyboard too.</summary>
public sealed class CalculatorFlyout : Border
{
    private readonly CalcEngine _calc = new();
    private readonly TextBlock _display = new() { FontSize = 34, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _history = new() { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right, MinHeight = 18 };

    public event EventHandler? CloseRequested;

    public CalculatorFlyout()
    {
        Width = 300;
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(14, 10, 14, 14);
        BorderThickness = new Thickness(1);
        Focusable = true;
        FocusVisualStyle = null;
        SetResourceReference(BackgroundProperty, "Brush.Surface");
        SetResourceReference(BorderBrushProperty, "Brush.Border");
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.3 };
        _display.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _display.FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont");
        _history.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        AutomationPropertiesName("Calculator");

        var stack = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var close = SmallButton("", "Close (Esc)", () => CloseRequested?.Invoke(this, EventArgs.Empty));
        var copy = SmallButton("", "Copy the result", () =>
        {
            if (_calc.IsError) return;
            Clipboard.SetText(_calc.Display);
            Toaster.Show("Copied " + _calc.Display);
        });
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(copy, Dock.Right);
        top.Children.Add(close);
        top.Children.Add(copy);
        top.Children.Add(Ui.Text("Calculator", muted: true, weight: FontWeights.SemiBold).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        stack.Children.Add(top);
        stack.Children.Add(_history);
        stack.Children.Add(new Viewbox { Child = _display, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Right, Height = 46, Margin = new Thickness(0, 0, 0, 10) });

        string[][] rows =
        [
            ["C", "±", "%", "÷"],
            ["7", "8", "9", "×"],
            ["4", "5", "6", "−"],
            ["1", "2", "3", "+"],
            ["⌫", "0", ".", "="],
        ];
        foreach (var row in rows)
        {
            var grid = new UniformGrid { Columns = 4 };
            foreach (var k in row) grid.Children.Add(Key(k));
            stack.Children.Add(grid);
        }
        Child = stack;
        Refresh();
        PreviewKeyDown += OnKey;
        PreviewTextInput += OnText;
    }

    private void AutomationPropertiesName(string name) => System.Windows.Automation.AutomationProperties.SetName(this, name);

    private Button SmallButton(string glyph, string tip, Action onClick)
    {
        var b = new Button { Style = (Style)Application.Current.FindResource("BaseButton"), Padding = new Thickness(6), ToolTip = tip, Focusable = false };
        b.Content = new TextBlock { Text = glyph, Style = (Style)Application.Current.FindResource("Icon"), FontSize = 13 }.Also(t => t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted"));
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => onClick();
        return b;
    }

    private Button Key(string k)
    {
        bool op = k is "C" or "±" or "%" or "÷" or "×" or "−" or "+";
        bool eq = k == "=";
        var text = new TextBlock { Text = k, FontSize = 18, FontWeight = op || eq ? FontWeights.SemiBold : FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, eq ? "Brush.AccentText" : op ? "Brush.Accent" : "Brush.Text");
        var face = new Border { CornerRadius = new CornerRadius(10), Height = 46, Margin = new Thickness(3), Child = text };
        face.SetResourceReference(Border.BackgroundProperty, eq ? "Brush.Accent" : op ? "Brush.AccentSoft" : "Brush.SurfaceAlt");
        var b = new Button { Content = face, Style = (Style)Application.Current.FindResource("CardButton"), Focusable = false, Cursor = Cursors.Hand };
        System.Windows.Automation.AutomationProperties.SetName(b, k == "⌫" ? "Backspace" : k);
        b.Click += (_, _) => Press(k);
        return b;
    }

    public void Press(string k)
    {
        switch (k)
        {
            case "C": _calc.Clear(); break;
            case "±": _calc.Negate(); break;
            case "%": _calc.Percent(); break;
            case "=": _calc.Evaluate(); break;
            case "⌫": _calc.Back(); break;
            case "+" or "−" or "×" or "÷": _calc.Op(k); break;
            default: _calc.Digit(k); break;
        }
        Refresh();
    }

    public string Display => _calc.Display;

    private void Refresh()
    {
        _display.Text = _calc.Display;
        _history.Text = _calc.History;
    }

    private void OnText(object sender, TextCompositionEventArgs e)
    {
        string? k = e.Text switch
        {
            "+" => "+", "-" => "−", "*" or "x" => "×", "/" => "÷", "%" => "%", "=" => "=",
            "." or "," => ".",
            "c" or "C" => "C",
            var t when t.Length == 1 && char.IsDigit(t[0]) => t,
            _ => null,
        };
        if (k == null) return;
        e.Handled = true;
        Press(k);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case System.Windows.Input.Key.Enter: e.Handled = true; Press("="); break;
            case System.Windows.Input.Key.Back: e.Handled = true; Press("⌫"); break;
            case System.Windows.Input.Key.Delete: e.Handled = true; Press("C"); break;
            case System.Windows.Input.Key.Escape: e.Handled = true; CloseRequested?.Invoke(this, EventArgs.Empty); break;
        }
    }
}
