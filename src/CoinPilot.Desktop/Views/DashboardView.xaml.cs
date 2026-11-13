using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>This month at a glance: left to spend, spent, income, budgets, spending by week and recent transactions.</summary>
public partial class DashboardView : UserControl
{
    private const int MaxBudgets = 6;
    private const int MaxRecent = 6;

    public DashboardView()
    {
        InitializeComponent();
        Loaded += (_, _) => { DataStore.Changed += OnDataChanged; Refresh(); };
        Unloaded += (_, _) => DataStore.Changed -= OnDataChanged;
        Refresh();
    }

    private void OnDataChanged(object? sender, EventArgs e) => Refresh();

    public void Refresh()
    {
        var now = DateTime.Now;
        var month = Budgeting.MonthStart(now);
        var summary = Budgeting.Summarize(month);

        MonthText.Text = Money.MonthName(month);
        ChartMonthText.Text = month.ToString("MMMM", Money.Us);
        GreetingText.Text = now.Hour switch
        {
            < 12 => "Good morning",
            < 18 => "Good afternoon",
            _ => "Good evening",
        };

        BuildSummary(summary, now);
        BuildBudgets(summary);
        BuildWeekChart(summary, now);
        BuildRecent();
    }

    // ----- Summary cards -----

    private void BuildSummary(MonthSummary s, DateTime now)
    {
        int daysLeft = DateTime.DaysInMonth(now.Year, now.Month) - now.Day;
        string days = daysLeft == 0 ? "last day of the month" : daysLeft == 1 ? "1 day left" : $"{daysLeft} days left";

        if (s.Budgets.Count == 0)
        {
            LeftText.Text = "—";
            LeftCaption.Text = "Set up a budget to see this";
        }
        else
        {
            LeftText.Text = Money.Format(s.LeftToSpend);
            LeftCaption.Text = s.LeftToSpend < 0 ? $"Over budget · {days}"
                : s.OverCount > 0 ? $"{s.OverCount} over budget · {days}"
                : $"On track · {days}";
        }

        SpentText.Text = Money.Format(s.Spent);
        int spendCount = s.Transactions.Count(t => t.Amount < 0);
        SpentCaption.Text = s.Budgets.Count > 0
            ? $"of {Money.Format(s.Budgeted, cents: false)} budgeted"
            : spendCount == 1 ? "1 purchase" : $"{spendCount} purchases";

        IncomeText.Text = Money.Format(s.Income);
        int incomeCount = s.Transactions.Count(t => t.Amount > 0);
        IncomeCaption.Text = incomeCount == 1 ? "1 deposit" : $"{incomeCount} deposits";

        NetText.Text = Money.Format(s.Net);
        NetText.SetResourceReference(TextBlock.ForegroundProperty, s.Net < 0 ? "Brush.Danger" : "Brush.Text");
    }

    // ----- Budgets card -----

    private void BuildBudgets(MonthSummary s)
    {
        BudgetList.Children.Clear();
        if (s.Budgets.Count == 0)
        {
            BudgetList.Children.Add(Ui.EmptyState("", "No budgets yet",
                "Set monthly amounts for categories like groceries and dining out.",
                "Create a budget", (_, _) => BudgetDialog.Edit(null)));
            return;
        }

        // Show the ones closest to (or over) their limit first.
        foreach (var b in s.Budgets.OrderByDescending(b => b.Fraction).ThenByDescending(b => b.Spent).Take(MaxBudgets))
        {
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            var amounts = Ui.Text($"{Money.Format(b.Spent, cents: false)} / {Money.Format(b.Limit, cents: false)}", muted: true);
            DockPanel.SetDock(amounts, Dock.Right);
            top.Children.Add(amounts);
            top.Children.Add(Ui.Text(b.Budget.Category, weight: FontWeights.SemiBold));
            row.Children.Add(top);
            row.Children.Add(Ui.ProgressBar(b.Fraction, b.BrushKey));
            if (b.IsOver)
            {
                var over = Ui.Text($"{Money.Format(-b.Left)} over budget", size: 12, brushKey: "Brush.Danger");
                over.Margin = new Thickness(0, 5, 0, 0);
                row.Children.Add(over);
            }
            BudgetList.Children.Add(row);
        }

        if (s.Budgets.Count > MaxBudgets)
        {
            var more = Ui.Text($"+ {s.Budgets.Count - MaxBudgets} more", muted: true, size: 12.5);
            more.Margin = new Thickness(0, 6, 0, 0);
            BudgetList.Children.Add(more);
        }
    }

    // ----- Weekly spending bar chart -----

    private void BuildWeekChart(MonthSummary s, DateTime now)
    {
        WeekChart.Children.Clear();
        WeekChart.ColumnDefinitions.Clear();

        var weeks = Budgeting.WeeklySpending(s);
        decimal max = Math.Max(weeks.Max(), 1);
        int currentWeek = (now.Day - 1) / 7;
        const double maxBar = 140;

        for (int i = 0; i < weeks.Length; i++)
        {
            WeekChart.ColumnDefinitions.Add(new ColumnDefinition());
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 8, 0) };

            var value = Ui.Text(Money.Format(weeks[i], cents: false), muted: true, size: 12.5);
            value.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(value);

            bool future = i > currentWeek;
            var bar = new Border
            {
                Height = Math.Max(4, Math.Round((double)(weeks[i] / max) * maxBar)),
                MaxWidth = 56,
                CornerRadius = new CornerRadius(9, 9, 3, 3),
                Margin = new Thickness(0, 7, 0, 8),
                ToolTip = $"Days {i * 7 + 1}–{Math.Min(i * 7 + 7, DateTime.DaysInMonth(now.Year, now.Month))}: {Money.Format(weeks[i])}"
                          + (i == currentWeek ? " so far" : ""),
            };
            bar.SetResourceReference(Border.BackgroundProperty, i == currentWeek || future ? "Brush.BarMuted" : "Brush.Accent");
            if (weeks[i] == 0) bar.SetResourceReference(Border.BackgroundProperty, "Brush.Track");
            col.Children.Add(bar);

            var label = Ui.Text($"Wk {i + 1}", weight: FontWeights.SemiBold, size: 12.5);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(label);

            Grid.SetColumn(col, i);
            WeekChart.Children.Add(col);
        }
    }

    // ----- Recent transactions -----

    private void BuildRecent()
    {
        RecentRows.Children.Clear();
        var recent = DataStore.Transactions.Items
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt)
            .Take(MaxRecent).ToList();

        RecentEmpty.Content = recent.Count > 0 ? null
            : Ui.EmptyState("", "No transactions yet",
                "Add your first purchase or paycheck and your dashboard fills in.",
                "Add transaction", Add_Click);

        foreach (var t in recent)
        {
            var g = Ui.Columns(2.2, 1.6, 1.3, 1.1, 1.1);
            var cell = new Thickness(8, 12, 8, 12);
            g.Children.Add(Ui.Text(t.Payee, weight: FontWeights.SemiBold).At(0, cell));
            g.Children.Add(Ui.Chip(t.Category).At(1, cell));
            g.Children.Add(Ui.Text(t.Account, muted: true).At(2, cell));
            g.Children.Add(Ui.Text(t.Date.ToString("MMM d", Money.Us), muted: true).At(3, cell));
            var amount = Ui.Text(Money.Signed(t.Amount), weight: FontWeights.SemiBold, brushKey: t.IsIncome ? "Brush.Positive" : "Brush.Text").At(4, cell);
            amount.HorizontalAlignment = HorizontalAlignment.Right;
            g.Children.Add(amount);

            var row = new Button { Content = g, Style = (Style)FindResource("RowButton"), ToolTip = "Edit or delete" };
            System.Windows.Automation.AutomationProperties.SetName(row, $"{t.Payee}, {Money.Signed(t.Amount)}, {t.Date:MMM d}");
            row.Click += (_, _) => TransactionDialog.Edit(t);
            RecentRows.Children.Add(row);
        }
    }

    // ----- Actions -----

    private void Add_Click(object sender, RoutedEventArgs e) => TransactionDialog.Edit(null, DateTime.Today);

    private void GoTransactions_Click(object sender, RoutedEventArgs e)
    {
        MonthSwitcher.Current = Budgeting.MonthStart(DateTime.Today);
        MainWindow.Instance?.GoTo("transactions");
    }

    private void GoBudgets_Click(object sender, RoutedEventArgs e)
    {
        MonthSwitcher.Current = Budgeting.MonthStart(DateTime.Today);
        MainWindow.Instance?.GoTo("budgets");
    }
}
