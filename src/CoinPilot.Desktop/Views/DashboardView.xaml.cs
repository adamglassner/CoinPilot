using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>
/// The Dashboard preview from the CoinPilot mockup. Everything on it is sample data;
/// the real numbers arrive with Transactions, Budgets and Goals.
/// </summary>
public partial class DashboardView : UserControl
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    private sealed record SampleBudget(string Name, decimal Spent, decimal Limit);
    private sealed record SampleTransaction(string Merchant, string Category, string Account, int DaysAgo, decimal Amount);

    private static readonly SampleBudget[] Budgets =
    [
        new("Groceries", 412, 600),
        new("Dining out", 236, 250),
        new("Transportation", 148, 300),
        new("Entertainment", 95, 150),
        new("Shopping", 310, 280),
    ];

    private static readonly (string Label, decimal Amount)[] Weeks =
    [
        ("Wk 1", 520), ("Wk 2", 610), ("Wk 3", 480), ("Wk 4", 390), ("Wk 5", 215),
    ];

    private static readonly SampleTransaction[] Transactions =
    [
        new("Fresh Thyme Market", "Groceries", "Checking", 0, -86.42m),
        new("Paycheck", "Income", "Checking", 0, 2050.00m),
        new("Shell", "Transportation", "Credit card", 1, -41.10m),
        new("Taco Bros", "Dining out", "Credit card", 2, -27.85m),
        new("Streaming service", "Entertainment", "Credit card", 3, -15.49m),
    ];

    public DashboardView()
    {
        InitializeComponent();

        var now = DateTime.Now;
        MonthText.Text = now.ToString("MMMM yyyy", Us);
        ChartMonthText.Text = now.ToString("MMMM", Us);
        GreetingText.Text = now.Hour switch
        {
            < 12 => "Good morning",
            < 18 => "Good afternoon",
            _ => "Good evening",
        };
        int daysLeft = DateTime.DaysInMonth(now.Year, now.Month) - now.Day;
        DaysLeftText.Text = daysLeft == 1 ? "On track · 1 day left" : $"On track · {daysLeft} days left";

        BuildBudgets();
        BuildWeekChart();
        BuildTransactions();
    }

    // ----- Budgets: name, spent / limit, progress bar -----

    private void BuildBudgets()
    {
        foreach (var b in Budgets)
        {
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };

            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            var amounts = Text($"{Money(b.Spent, 0)} / {Money(b.Limit, 0)}", muted: true);
            DockPanel.SetDock(amounts, Dock.Right);
            top.Children.Add(amounts);
            top.Children.Add(Text(b.Name, weight: FontWeights.SemiBold));
            row.Children.Add(top);

            double pct = Math.Min(1.0, (double)(b.Spent / b.Limit));
            string fill = b.Spent > b.Limit ? "Brush.Danger" : pct >= 0.85 ? "Brush.Warn" : "Brush.Accent";
            row.Children.Add(ProgressBar(pct, fill, "Brush.Track", 10));

            if (b.Spent > b.Limit)
            {
                var over = Text($"{Money(b.Spent - b.Limit, 0)} over budget", size: 12);
                over.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
                over.Margin = new Thickness(0, 5, 0, 0);
                row.Children.Add(over);
            }

            BudgetList.Children.Add(row);
        }
    }

    /// <summary>A rounded track with a filled part, sized with star columns so it stretches with the card.</summary>
    private static Grid ProgressBar(double fraction, string fillKey, string trackKey, double height)
    {
        var grid = new Grid { Height = height };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(fraction, 0.0001), GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - fraction, 0.0001), GridUnitType.Star) });

        var track = new Border { CornerRadius = new CornerRadius(height / 2) };
        track.SetResourceReference(Border.BackgroundProperty, trackKey);
        Grid.SetColumnSpan(track, 2);
        grid.Children.Add(track);

        var fill = new Border { CornerRadius = new CornerRadius(height / 2) };
        fill.SetResourceReference(Border.BackgroundProperty, fillKey);
        grid.Children.Add(fill);
        return grid;
    }

    // ----- Weekly spending bar chart -----

    private void BuildWeekChart()
    {
        decimal max = Weeks.Max(w => w.Amount);
        const double maxBar = 140;
        for (int i = 0; i < Weeks.Length; i++)
        {
            var (label, amount) = Weeks[i];
            WeekChart.ColumnDefinitions.Add(new ColumnDefinition());

            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 8, 0) };
            var value = Text(Money(amount, 0), muted: true, size: 12.5);
            value.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(value);

            bool current = i == Weeks.Length - 1; // the week still in progress
            var bar = new Border
            {
                Height = Math.Round((double)(amount / max) * maxBar),
                MaxWidth = 56,
                CornerRadius = new CornerRadius(9, 9, 3, 3),
                Margin = new Thickness(0, 7, 0, 8),
                ToolTip = current ? $"{label}: {Money(amount, 2)} so far" : $"{label}: {Money(amount, 2)}",
            };
            bar.SetResourceReference(Border.BackgroundProperty, current ? "Brush.BarMuted" : "Brush.Accent");
            col.Children.Add(bar);

            var name = Text(label, weight: FontWeights.SemiBold, size: 12.5);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(name);

            Grid.SetColumn(col, i);
            WeekChart.Children.Add(col);
        }
    }

    // ----- Recent transactions table -----

    private void BuildTransactions()
    {
        string[] headers = ["MERCHANT", "CATEGORY", "ACCOUNT", "DATE", "AMOUNT"];
        double[] widths = [2.2, 1.6, 1.3, 1.1, 1.1];
        foreach (var w in widths) TxTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });

        TxTable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int c = 0; c < headers.Length; c++)
        {
            var h = Text(headers[c], muted: true, size: 11, weight: FontWeights.SemiBold);
            h.Margin = new Thickness(4, 8, 4, 10);
            if (c == headers.Length - 1) h.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(h, c);
            TxTable.Children.Add(h);
        }

        var today = DateTime.Today;
        for (int r = 0; r < Transactions.Length; r++)
        {
            var t = Transactions[r];
            int row = r + 1;
            TxTable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0) };
            line.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            Grid.SetRow(line, row);
            Grid.SetColumnSpan(line, headers.Length);
            TxTable.Children.Add(line);

            Add(Text(t.Merchant, weight: FontWeights.SemiBold), row, 0);

            var chip = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 3, 10, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = Text(t.Category, size: 12.5),
            };
            chip.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
            Add(chip, row, 1);

            Add(Text(t.Account, muted: true), row, 2);
            Add(Text(today.AddDays(-t.DaysAgo).ToString("MMM d", Us), muted: true), row, 3);

            var amount = Text((t.Amount > 0 ? "+" : "−") + Money(Math.Abs(t.Amount), 2), weight: FontWeights.SemiBold);
            amount.HorizontalAlignment = HorizontalAlignment.Right;
            amount.SetResourceReference(TextBlock.ForegroundProperty, t.Amount > 0 ? "Brush.Positive" : "Brush.Text");
            Add(amount, row, 4);
        }
    }

    private void Add(FrameworkElement element, int row, int column)
    {
        element.Margin = new Thickness(4, 13, 4, 13);
        element.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        TxTable.Children.Add(element);
    }

    // ----- Helpers -----

    private static string Money(decimal value, int decimals) =>
        value.ToString(decimals == 0 ? "$#,0" : "$#,0.00", Us);

    private static TextBlock Text(string text, bool muted = false, double size = 13.5, FontWeight? weight = null)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, muted ? "Brush.TextMuted" : "Brush.Text");
        return tb;
    }

    private void Soon_Click(object sender, RoutedEventArgs e) => Toaster.Soon(sender);

    private void ViewAll_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.GoTo("transactions");

    private void Search_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Toaster.Show("Search is coming soon.");
    }
}
