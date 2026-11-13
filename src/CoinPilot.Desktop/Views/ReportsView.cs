using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>
/// The Reports page: income vs. spending, spending by category, net worth over time and a month-by-month table
/// for the last 3/6/12 months or this year, plus a Year in review. Export to CSV or PDF.
/// </summary>
public sealed class ReportsView : DataPage
{
    public const string ThreeMonths = "3 months", SixMonths = "6 months", TwelveMonths = "12 months", ThisYear = "This year", YearInReview = "Year in review";
    public static readonly string[] Ranges = [ThreeMonths, SixMonths, TwelveMonths, ThisYear, YearInReview];

    /// <summary>The period shown; remembered while the app is open.</summary>
    public static string Range { get; set; } = SixMonths;

    /// <summary>The year shown in Year in review.</summary>
    public static int ReviewYear { get; set; } = DateTime.Today.Year;

    protected override void Build()
    {
        Add(PageKit.Header("", "Reports", "See where your money goes over time."));

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var pdf = PageKit.Ghost("", "Print / PDF", (_, _) => Print());
        pdf.ToolTip = "Print this report, or choose \"Microsoft Print to PDF\" to save it as a PDF.";
        pdf.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(pdf);
        var export = PageKit.Ghost("", "Export CSV", (_, _) => Export());
        buttons.Children.Add(export);
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        bar.Children.Add(PageKit.ChoiceChips(Ranges, Range, pick => { Range = pick; Rebuild(); }));
        Add(bar);

        foreach (var section in Range == YearInReview ? ReviewSections(forPrint: false) : PeriodSections(forPrint: false))
            Add(section);
    }

    // ----- Period reports -----

    private static (List<MonthTotals> Months, DateTime From, DateTime To) Period()
    {
        var today = DateTime.Today;
        var thisMonth = Budgeting.MonthStart(today);
        int count = Range switch
        {
            ThreeMonths => 3,
            TwelveMonths => 12,
            ThisYear => Math.Max(1, Enumerable.Range(0, 13).TakeWhile(i => thisMonth.AddMonths(-i).Year == today.Year).Count()),
            _ => 6,
        };
        var months = Reports.Monthly(thisMonth, count, DataStore.Transactions.Items);
        return (months, months[0].Month, today);
    }

    private List<UIElement> PeriodSections(bool forPrint)
    {
        var list = new List<UIElement>();
        var (months, from, to) = Period();
        var categories = Reports.ByCategory(from, to, DataStore.Transactions.Items);

        decimal income = months.Sum(m => m.Income), spent = months.Sum(m => m.Spent);
        string rate = income > 0 ? ((income - spent) / income).ToString("P0", Money.Us) : "—";
        if (forPrint) list.Add(PrintTitle($"WillowWallet report · {from:MMM d, yyyy} – {to:MMM d, yyyy}"));
        list.Add(PageKit.KpiRow(
            PageKit.Kpi("Average monthly spending", Money.Format(spent / months.Count), $"{Money.Format(spent, cents: false)} total", hero: true),
            PageKit.Kpi("Average monthly income", Money.Format(income / months.Count), $"{Money.Format(income, cents: false)} total"),
            PageKit.Kpi("Savings rate", rate, "Share of income not spent", valueBrush: income > 0 && spent > income ? "Brush.Danger" : null),
            PageKit.Kpi("Net worth now", Money.Format(AccountMath.NetWorth()), "Accounts minus debts")));

        if (income == 0 && spent == 0)
        {
            list.Add(PageKit.Card(Ui.EmptyState("", "Nothing to report yet",
                $"There are no transactions from {from:MMMM yyyy} to now. Add or import transactions and your reports fill in.",
                forPrint ? null : "Add transaction", forPrint ? null : (_, _) => TransactionDialog.Edit(null, DateTime.Today))));
            return list;
        }

        list.Add(PageKit.Card(IncomeVsSpendingChart(months), "Income vs. spending", Legend()));
        list.Add(PageKit.Card(CategoryBars(categories, spent), $"Spending by category · {from:MMM yyyy} – {to:MMM yyyy}"));
        if (DataStore.Accounts.Items.Count > 0)
        {
            var history = AccountMath.NetWorthHistory(Math.Max(3, months.Count));
            list.Add(PageKit.Card(Charts.LineChart(history.Select(p => (p.Date.ToString("MMM", Money.Us), p.Value)).ToList(), 180), "Net worth"));
        }
        list.Add(PageKit.Card(MonthTable(months), "Month by month", padding: new Thickness(22, 20, 22, 10)));
        return list;
    }

    // ----- Year in review -----

    private List<UIElement> ReviewSections(bool forPrint)
    {
        var list = new List<UIElement>();
        var years = YearReview.Years();
        if (!years.Contains(ReviewYear)) ReviewYear = years[0];
        var y = YearReview.For(ReviewYear);

        if (forPrint) list.Add(PrintTitle($"WillowWallet · {y.Year} in review"));
        else
        {
            var yearBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            yearBar.Children.Add(Ui.Text("Year", muted: true).Also(t => t.Margin = new Thickness(0, 0, 10, 0)));
            yearBar.Children.Add(PageKit.ChoiceChips(years.Select(v => v.ToString()), ReviewYear.ToString(), pick => { ReviewYear = int.Parse(pick); Rebuild(); }));
            list.Add(yearBar);
        }

        list.Add(PageKit.KpiRow(
            PageKit.Kpi($"Spent in {y.Year}", Money.Format(y.Spent, cents: false), $"{y.TransactionCount:N0} transactions", hero: true),
            PageKit.Kpi("Earned", Money.Format(y.Income, cents: false)),
            PageKit.Kpi("Kept", Money.Format(y.Net, cents: false), y.SavingsRate is double r ? $"{r:P0} of income" : null, valueBrush: y.Net < 0 ? "Brush.Danger" : null),
            PageKit.Kpi("Debt paid down", Money.Format(y.DebtPaid, cents: false), y.SavedToGoals > 0 ? $"{Money.Format(y.SavedToGoals, cents: false)} added to goals" : null)));

        if (y.TransactionCount == 0)
        {
            list.Add(PageKit.Card(Ui.EmptyState("", $"No transactions in {y.Year}", "Once you've added transactions for the year, your review appears here.")));
            return list;
        }

        // Highlights
        var best = y.Months.Where(m => m.Income > 0 || m.Spent > 0).OrderByDescending(m => m.Net).FirstOrDefault();
        var priciest = y.Months.OrderByDescending(m => m.Spent).FirstOrDefault(m => m.Spent > 0);
        var highlights = new StackPanel();
        void Line(string glyph, string text)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
            var icon = new TextBlock { Text = glyph, Style = Res("Icon"), FontSize = 15, Width = 28 };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            row.Children.Add(icon);
            row.Children.Add(Ui.Text(text).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }));
            highlights.Children.Add(row);
        }
        if (best != null) Line("", $"Best month: {best.Month:MMMM}, when you kept {Money.Format(best.Net, cents: false)}.");
        if (priciest != null) Line("", $"Biggest spending month: {priciest.Month:MMMM} ({Money.Format(priciest.Spent, cents: false)}).");
        if (y.TopCategories.Count > 0) Line("", $"Most went to {Categories.Display(y.TopCategories[0].Category)}: {Money.Format(y.TopCategories[0].Spent, cents: false)}.");
        if (y.BiggestPurchase is Transaction big) Line("", $"Largest purchase: {big.Payee}, {Money.Format(big.Size)} on {big.Date:MMM d}.");
        if (y.BillsPaid > 0) Line("", $"{y.BillsPaid} bill{(y.BillsPaid == 1 ? "" : "s")} marked paid on time in WillowWallet.");
        list.Add(PageKit.Card(highlights, "Highlights"));

        list.Add(PageKit.Card(IncomeVsSpendingChart(y.Months), "Month by month", Legend()));

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.Children.Add(PageKit.Card(CategoryBars(y.TopCategories, y.Spent), "Top categories", bottom: 0));
        var payees = new StackPanel();
        foreach (var (payee, amount, count) in y.TopPayees)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            var a = Ui.Text(Money.Format(amount, cents: false), weight: FontWeights.SemiBold);
            DockPanel.SetDock(a, Dock.Right);
            row.Children.Add(a);
            var name = new StackPanel();
            name.Children.Add(Ui.Text(payee, weight: FontWeights.SemiBold));
            name.Children.Add(Ui.Text(count == 1 ? "1 purchase" : $"{count} purchases", muted: true, size: 12));
            row.Children.Add(name);
            payees.Children.Add(row);
        }
        columns.Children.Add(PageKit.Card(payees, "Where you spent most", bottom: 0).At(2));
        columns.Margin = new Thickness(0, 0, 0, 18);
        list.Add(columns);
        return list;
    }

    // ----- Pieces -----

    private static TextBlock PrintTitle(string text) => new()
    {
        Text = text, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 16),
        FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
    };

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
        var grid = new Grid { Height = 214 };
        System.Windows.Automation.AutomationProperties.SetName(grid, "Income and spending by month");

        // Light gridlines behind the bars
        var lines = new Grid { Margin = new Thickness(0, 0, 0, 26) };
        for (int i = 0; i < 4; i++)
        {
            var l = new Border { BorderThickness = new Thickness(0, 1, 0, 0), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, maxBar / 4 * (i + 1)) };
            l.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            lines.Children.Add(l);
        }
        Grid.SetColumnSpan(lines, Math.Max(1, months.Count));
        grid.Children.Add(lines);

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
                    ToolTip = $"{Money.MonthName(m.Month)} {what.ToLowerInvariant()}: {Money.Format(value)}",
                };
                b.SetResourceReference(Border.BackgroundProperty, value == 0 ? "Brush.Track" : brush);
                bars.Children.Add(b);
            }
            col.Children.Add(bars);

            var label = Ui.Text(Money.MonthShort(m.Month, withYear: m.Month.Month == 1 || i == 0), muted: true, size: 12);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 8, 0, 0);
            col.Children.Add(label);

            Grid.SetColumn(col, i);
            grid.Children.Add(col);
        }
        return grid;
    }

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

            row.Children.Add(Ui.Text(Categories.Display(category), weight: FontWeights.SemiBold));
            var bar = Ui.ProgressBar(max <= 0 ? 0 : (double)(amount / max), "Brush.Accent", "Brush.Track", 12);
            bar.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(bar.At(1, new Thickness(8, 0, 12, 0)));
            var value = Ui.Text($"{Money.Format(amount)}  ·  {(total <= 0 ? 0 : amount / total).ToString("P0", Money.Us)}", muted: true).At(2);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            row.Children.Add(value);
            panel.Children.Add(row);
        }
        return panel;
    }

    private static StackPanel MonthTable(List<MonthTotals> months)
    {
        var panel = new StackPanel();
        double[] cols = [1.6, 1, 1, 1, 1];
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
            row.Children.Add(Ui.Text(Money.MonthName(m.Month), weight: FontWeights.SemiBold).At(0, cell));
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

    // ----- Export -----

    private static void Export()
    {
        if (Range == YearInReview)
        {
            var y = YearReview.For(ReviewYear);
            CsvExporter.Export($"coinpilot-{y.Year}-in-review", ["Month", "Income", "Spending", "Net"],
                y.Months.Select(m => new[] { m.Month.ToString("yyyy-MM"), m.Income.ToString("0.00", Money.Us), m.Spent.ToString("0.00", Money.Us), m.Net.ToString("0.00", Money.Us) }));
            return;
        }
        var (months, _, _) = Period();
        CsvExporter.Export("coinpilot-report",
            ["Month starting", "Income", "Spending", "Net", "Savings rate"],
            months.Select(m => new[]
            {
                m.Month.ToString("yyyy-MM-dd"),
                m.Income.ToString("0.00", Money.Us), m.Spent.ToString("0.00", Money.Us), m.Net.ToString("0.00", Money.Us),
                m.SavingsRate is double r ? r.ToString("0.0%", Money.Us) : "",
            }));
    }

    /// <summary>
    /// Prints the report (choose "Microsoft Print to PDF" to save a PDF). The report is laid out on letter-size pages
    /// in the light theme so it looks right on paper, then the screen theme is restored.
    /// </summary>
    private void Print()
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;

        string theme = App.Settings.Theme;
        bool wasDark = App.IsDark;
        if (wasDark) App.ApplyTheme(AppTheme.Light);
        try
        {
            var size = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            var doc = BuildDocument(size, Range == YearInReview ? ReviewSections(forPrint: true) : PeriodSections(forPrint: true));
            dialog.PrintDocument(doc.DocumentPaginator, "WillowWallet report");
            Toaster.Show("Report sent to the printer.");
        }
        catch (Exception ex)
        {
            Toaster.Show($"Couldn't print: {ex.Message}");
        }
        finally
        {
            if (wasDark) App.ApplyTheme(theme);
        }
    }

    /// <summary>Flows the sections onto as many pages as needed.</summary>
    public static FixedDocument BuildDocument(Size pageSize, List<UIElement> sections)
    {
        const double margin = 48;
        double width = pageSize.Width - margin * 2, height = pageSize.Height - margin * 2;
        var doc = new FixedDocument();
        doc.DocumentPaginator.PageSize = pageSize;

        FixedPage NewPage(out StackPanel body)
        {
            var page = new FixedPage { Width = pageSize.Width, Height = pageSize.Height };
            page.SetResourceReference(FixedPage.BackgroundProperty, "Brush.Bg");
            body = new StackPanel { Width = width };
            FixedPage.SetLeft(body, margin);
            FixedPage.SetTop(body, margin);
            page.Children.Add(body);
            var footer = new TextBlock { Text = $"WillowWallet · printed {DateTime.Now:MMM d, yyyy}", FontSize = 9, Foreground = Brushes.Gray };
            FixedPage.SetLeft(footer, margin);
            FixedPage.SetTop(footer, pageSize.Height - margin + 14);
            page.Children.Add(footer);
            return page;
        }

        var current = NewPage(out var body);
        double used = 0;
        foreach (var section in sections)
        {
            section.Measure(new Size(width, double.PositiveInfinity));
            double h = section.DesiredSize.Height;
            if (used > 0 && used + h > height)
            {
                doc.Pages.Add(new PageContent { Child = current });
                current = NewPage(out body);
                used = 0;
            }
            body.Children.Add(section);
            used += h;
        }
        doc.Pages.Add(new PageContent { Child = current });
        foreach (var page in doc.Pages)
        {
            page.Child.Measure(pageSize);
            page.Child.Arrange(new Rect(pageSize));
            page.Child.UpdateLayout();
        }
        return doc;
    }
}
