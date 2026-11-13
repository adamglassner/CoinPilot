using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>The Reports page: income vs. spending by month, spending by category, and a monthly table you can export.</summary>
public sealed class ReportsView : DataPage
{
    public const string ThreeMonths = "3 months", SixMonths = "6 months", TwelveMonths = "12 months", ThisYear = "This year";
    public static readonly string[] Ranges = [ThreeMonths, SixMonths, TwelveMonths, ThisYear];

    /// <summary>The period shown; remembered while the app is open.</summary>
    public static string Range { get; set; } = SixMonths;

    protected override void Build()
    {
        var today = DateTime.Today;
        var thisMonth = Budgeting.MonthStart(today);
        int count = Range switch
        {
            ThreeMonths => 3,
            TwelveMonths => 12,
            ThisYear => today.Month,
            _ => 6,
        };
        var months = Reports.Monthly(thisMonth, count, DataStore.Transactions.Items);
        var from = months[0].Month;
        var categories = Reports.ByCategory(from, today, DataStore.Transactions.Items);

        Add(PageKit.Header("", "Reports", "See where your money goes over time."));

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var export = PageKit.Ghost("", "Export", (_, _) => Export(months));
        export.IsEnabled = months.Any(m => m.Income > 0 || m.Spent > 0);
        DockPanel.SetDock(export, Dock.Right);
        bar.Children.Add(export);
        bar.Children.Add(PageKit.ChoiceChips(Ranges, Range, pick => { Range = pick; Rebuild(); }));
        Add(bar);

        decimal income = months.Sum(m => m.Income), spent = months.Sum(m => m.Spent);
        string rate = income > 0 ? $"{(income - spent) / income:P0}" : "—";
        Add(PageKit.KpiRow(
            PageKit.Kpi("Average monthly spending", Money.Format(spent / months.Count), $"{Money.Format(spent, cents: false)} total", hero: true),
            PageKit.Kpi("Average monthly income", Money.Format(income / months.Count), $"{Money.Format(income, cents: false)} total"),
            PageKit.Kpi("Savings rate", rate, "Share of income not spent", valueBrush: income > 0 && spent > income ? "Brush.Danger" : null),
            PageKit.Kpi("Net worth now", Money.Format(AccountMath.NetWorth()), "Accounts minus debts")));

        if (income == 0 && spent == 0)
        {
            Add(PageKit.Card(Ui.EmptyState("", "Nothing to report yet",
                $"There are no transactions from {from:MMMM yyyy} to now. Add transactions and your reports fill in.",
                "Add transaction", (_, _) => TransactionDialog.Edit(null, today))));
            return;
        }

        Add(PageKit.Card(IncomeVsSpendingChart(months), "Income vs. spending", Legend()));
        Add(PageKit.Card(CategoryBars(categories, spent), $"Spending by category · {from:MMM yyyy} – {today:MMM yyyy}"));
        Add(PageKit.Card(MonthTable(months), "Month by month", padding: new Thickness(22, 20, 22, 10)));
    }

    // ----- Chart: grouped bars per month -----

    private static StackPanel Legend()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (label, brush) in new[] { ("Income", "Brush.Accent"), ("Spending", "Brush.Warn") })
        {
            var swatch = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Margin = new Thickness(14, 0, 6, 0) };
            swatch.SetResourceReference(Border.BackgroundProperty, brush);
            panel.Children.Add(swatch);
            panel.Children.Add(Ui.Text(label, muted: true, size: 12.5));
        }
        return panel;
    }

    private static Grid IncomeVsSpendingChart(List<MonthTotals> months)
    {
        const double maxBar = 160;
        decimal max = Math.Max(1, months.Max(m => Math.Max(m.Income, m.Spent)));
        var grid = new Grid { Height = 210 };
        System.Windows.Automation.AutomationProperties.SetName(grid, "Income and spending by month");
        for (int i = 0; i < months.Count; i++)
        {
            var m = months[i];
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };

            var bars = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
            foreach (var (value, brush, what) in new[] { (m.Income, "Brush.Accent", "Income"), (m.Spent, "Brush.Warn", "Spending") })
            {
                var b = new Border
                {
                    Width = months.Count > 8 ? 12 : 22,
                    Height = Math.Max(3, Math.Round((double)(value / max) * maxBar)),
                    CornerRadius = new CornerRadius(5, 5, 2, 2),
                    Margin = new Thickness(2, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    ToolTip = $"{m.Month:MMMM yyyy} {what.ToLowerInvariant()}: {Money.Format(value)}",
                };
                b.SetResourceReference(Border.BackgroundProperty, value == 0 ? "Brush.Track" : brush);
                bars.Children.Add(b);
            }
            col.Children.Add(bars);

            var label = Ui.Text(m.Month.ToString(m.Month.Month == 1 || i == 0 ? "MMM yy" : "MMM", Money.Us), muted: true, size: 12);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 8, 0, 0);
            col.Children.Add(label);

            Grid.SetColumn(col, i);
            grid.Children.Add(col);
        }
        return grid;
    }

    // ----- Horizontal bars per category -----

    private static StackPanel CategoryBars(List<(string Category, decimal Spent)> categories, decimal total)
    {
        var panel = new StackPanel();
        if (categories.Count == 0)
        {
            panel.Children.Add(Ui.Text("No spending in this period.", muted: true));
            return panel;
        }
        decimal max = categories[0].Spent;
        foreach (var (category, amount) in categories)
        {
            var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

            row.Children.Add(Ui.Text(category, weight: FontWeights.SemiBold));
            var bar = Ui.ProgressBar(max <= 0 ? 0 : (double)(amount / max), "Brush.Accent", "Brush.Track", 12);
            bar.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(bar.At(1, new Thickness(8, 0, 12, 0)));
            var value = Ui.Text($"{Money.Format(amount)}  ·  {(total <= 0 ? 0 : amount / total):P0}", muted: true).At(2);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            row.Children.Add(value);
            panel.Children.Add(row);
        }
        return panel;
    }

    // ----- Table -----

    private static StackPanel MonthTable(List<MonthTotals> months)
    {
        var panel = new StackPanel();
        double[] cols = [1.4, 1, 1, 1, 1];
        var head = Ui.Columns(cols);
        head.Margin = new Thickness(0, 0, 0, 8);
        string[] names = ["MONTH", "INCOME", "SPENDING", "NET", "SAVED"];
        for (int i = 0; i < names.Length; i++)
        {
            var t = Ui.Text(names[i], muted: true, size: 11, weight: FontWeights.SemiBold).At(i, new Thickness(4, 0, 4, 0));
            if (i > 0) t.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(t);
        }
        panel.Children.Add(head);

        foreach (var m in Enumerable.Reverse(months))
        {
            var row = Ui.Columns(cols);
            var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0) };
            line.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            Grid.SetColumnSpan(line, cols.Length);
            row.Children.Add(line);
            var cell = new Thickness(4, 11, 4, 11);
            row.Children.Add(Ui.Text(m.Month.ToString("MMMM yyyy", Money.Us), weight: FontWeights.SemiBold).At(0, cell));
            string[] values =
            [
                Money.Format(m.Income), Money.Format(m.Spent), Money.Format(m.Net),
                m.SavingsRate is double r ? r.ToString("P0", Money.Us) : "—",
            ];
            for (int i = 0; i < values.Length; i++)
            {
                var t = Ui.Text(values[i], brushKey: i == 2 && m.Net < 0 ? "Brush.Danger" : "Brush.Text").At(i + 1, cell);
                t.HorizontalAlignment = HorizontalAlignment.Right;
                row.Children.Add(t);
            }
            panel.Children.Add(row);
        }
        return panel;
    }

    private static void Export(List<MonthTotals> months) =>
        CsvExporter.Export("coinpilot-report",
            ["Month", "Income", "Spending", "Net", "Savings rate"],
            months.Select(m => new[]
            {
                m.Month.ToString("yyyy-MM"),
                m.Income.ToString("0.00", Money.Us), m.Spent.ToString("0.00", Money.Us), m.Net.ToString("0.00", Money.Us),
                m.SavingsRate is double r ? r.ToString("0.0%", Money.Us) : "",
            }));
}
