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
        ChartMonthText.Text = Budgeting.StartDay == 1 ? month.ToString("MMMM", Money.Us) : Money.MonthName(month);
        GreetingText.Text = now.Hour switch
        {
            < 12 => "Good morning",
            < 18 => "Good afternoon",
            _ => "Good evening",
        };

        BuildWelcome();
        BuildSummary(summary, now);
        BuildBudgets(summary);
        BuildWeekChart(summary, now);
        BuildBills(now.Date);
        BuildGoals(now.Date);
        BuildRecent();
    }

    // ----- Getting started -----

    private void BuildWelcome()
    {
        var prefs = DataStore.Prefs;
        (string Title, string Detail, bool Done, Action Go)[] steps =
        [
            ("Add your accounts", "Checking, savings and cards, with today's balances.", DataStore.Accounts.Items.Count > 0, () => MainWindow.Instance?.GoTo("accounts")),
            ("Add or import transactions", "Type them in, or import a statement from your bank.", DataStore.Transactions.Items.Count > 0, () => MainWindow.Instance?.GoTo("transactions")),
            ("Set up your budgets", "Start from a template like 50/30/20.", DataStore.Budgets.Items.Count > 0, () => MainWindow.Instance?.GoTo("budgets")),
            ("Add your bills", "Get reminders before they're due.", DataStore.Bills.Items.Count > 0, () => MainWindow.Instance?.GoTo("bills")),
            ("Protect your data", "Add a PIN or password (optional).", DataLock.IsEnabled, () => MainWindow.Instance?.ShowSettings()),
        ];
        int done = steps.Count(s => s.Done);
        if (prefs.WelcomeDismissed || done == steps.Length)
        {
            WelcomeHost.Content = null;
            return;
        }

        var panel = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var dismiss = PageKit.Link("Hide", () => { prefs.WelcomeDismissed = true; prefs.Save(); });
        DockPanel.SetDock(dismiss, Dock.Right);
        head.Children.Add(dismiss);
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "Welcome to CoinPilot", Style = (Style)FindResource("CardTitle"), FontSize = 19 });
        titles.Children.Add(Ui.Text($"{done} of {steps.Length} done · a few minutes to get set up", muted: true));
        head.Children.Add(titles);
        panel.Children.Add(head);
        panel.Children.Add(Ui.ProgressBar(done / (double)steps.Length, "Brush.Accent", height: 6).Also(b => b.Margin = new Thickness(0, 0, 0, 12)));

        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = steps.Length, Margin = new Thickness(0, 0, -10, 0) };
        foreach (var step in steps)
        {
            var tile = new StackPanel();
            var icon = new TextBlock { Text = step.Done ? "\uE73E" : "\uE710", Style = (Style)FindResource("Icon"), FontSize = 15, Margin = new Thickness(0, 0, 0, 8) };
            icon.SetResourceReference(TextBlock.ForegroundProperty, step.Done ? "Brush.Positive" : "Brush.Accent");
            tile.Children.Add(icon);
            tile.Children.Add(Ui.Text(step.Title, weight: FontWeights.SemiBold).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }));
            tile.Children.Add(Ui.Text(step.Detail, muted: true, size: 12).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.Margin = new Thickness(0, 3, 0, 0); }));
            var card = new Border { Child = tile, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 14), Opacity = step.Done ? 0.6 : 1 };
            card.SetResourceReference(Border.BackgroundProperty, step.Done ? "Brush.SurfaceAlt" : "Brush.AccentSoft");
            var go = step.Go;
            var button = new Button { Content = card, Style = (Style)FindResource("CardButton"), Margin = new Thickness(0, 0, 10, 0), IsEnabled = !step.Done };
            System.Windows.Automation.AutomationProperties.SetName(button, step.Title);
            button.Click += (_, _) => go();
            grid.Children.Add(button);
        }
        panel.Children.Add(grid);
        WelcomeHost.Content = PageKit.Card(panel);
    }

    // ----- Summary cards -----

    private void BuildSummary(MonthSummary s, DateTime now)
    {
        int daysLeft = (Budgeting.MonthEnd(Budgeting.MonthStart(now)) - now.Date).Days;
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
        int spendCount = s.Transactions.Count(t => t.IsSpending);
        SpentCaption.Text = s.Budgets.Count > 0
            ? $"of {Money.Format(s.Budgeted, cents: false)} budgeted"
            : spendCount == 1 ? "1 purchase" : $"{spendCount} purchases";

        IncomeText.Text = Money.Format(s.Income);
        int incomeCount = s.Transactions.Count(t => t.IsIncome);
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
            top.Children.Add(Ui.Text(Categories.Display(b.Budget.Category), weight: FontWeights.SemiBold));
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
        int currentWeek = (now.Date - s.Month).Days / 7;
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
                ToolTip = $"{s.Month.AddDays(i * 7):MMM d}–{(s.Month.AddDays(Math.Min(i * 7 + 6, (s.Month.AddMonths(1) - s.Month).Days - 1))):MMM d}: {Money.Format(weeks[i])}"
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

    // ----- Coming up: bills due in the next 2 weeks (and anything overdue) -----

    private void BuildBills(DateTime today)
    {
        BillList.Children.Clear();
        var bills = DataStore.Bills.Items;
        if (bills.Count == 0)
        {
            BillList.Children.Add(Ui.EmptyState("\uE787", "No bills yet",
                "Add your bills and subscriptions to see what's due soon.",
                "Add bill", (_, _) => BillDialog.Edit(null)));
            return;
        }

        var soon = bills.Where(b => !b.IsDone && (b.NextDue.Date - today).Days <= 14).OrderBy(b => b.NextDue).Take(5).ToList();
        if (soon.Count == 0)
        {
            var next = bills.Where(b => !b.IsDone).OrderBy(b => b.NextDue).FirstOrDefault();
            var t = Ui.Text(next == null ? "Nothing due." : $"Nothing due in the next 2 weeks. Next up: {next.Name} on {next.NextDue:MMM d}.", muted: true);
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 8, 0, 8);
            BillList.Children.Add(t);
            return;
        }

        foreach (var b in soon)
        {
            var row = new DockPanel { Margin = new Thickness(0, 7, 0, 7) };
            var amount = Ui.Text(Money.Format(b.Amount), weight: FontWeights.SemiBold);
            amount.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(amount, Dock.Right);
            row.Children.Add(amount);

            var status = BillSchedule.Status(b, today);
            string brush = status == BillStatus.Overdue ? "Brush.Danger" : status is BillStatus.DueToday or BillStatus.DueSoon ? "Brush.Warn" : "Brush.TextMuted";
            var when = PageKit.Pill(BillSchedule.StatusText(b, today), brush);
            DockPanel.SetDock(when, Dock.Right);
            row.Children.Add(when);

            row.Children.Add(Ui.Text(b.Name, weight: FontWeights.SemiBold));
            BillList.Children.Add(row);
        }
    }

    // ----- Goals: the ones still in progress -----

    private void BuildGoals(DateTime today)
    {
        GoalList.Children.Clear();
        GoalMath.RefreshLinked();
        var goals = DataStore.Goals.Items;
        if (goals.Count == 0)
        {
            GoalList.Children.Add(Ui.EmptyState("\uE7C1", "No goals yet",
                "Save toward an emergency fund, a trip or anything else.",
                "New goal", (_, _) => GoalDialog.Edit(null)));
            return;
        }

        var show = goals.Where(g => !g.IsReached).OrderBy(g => g.TargetDate ?? DateTime.MaxValue).Take(4).ToList();
        if (show.Count == 0)
        {
            var t = Ui.Text($"All {goals.Count} goal{(goals.Count == 1 ? "" : "s")} reached. Time for a new one?", muted: true);
            t.Margin = new Thickness(0, 8, 0, 8);
            GoalList.Children.Add(t);
            return;
        }

        foreach (var g in show)
        {
            var row = new StackPanel { Margin = new Thickness(0, 7, 0, 7) };
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            var amounts = Ui.Text($"{Money.Format(g.Saved, cents: false)} / {Money.Format(g.Target, cents: false)}", muted: true);
            DockPanel.SetDock(amounts, Dock.Right);
            top.Children.Add(amounts);
            top.Children.Add(Ui.Text(g.Name, weight: FontWeights.SemiBold));
            row.Children.Add(top);
            row.Children.Add(Ui.ProgressBar(g.Fraction, "Brush.Accent"));
            if (GoalMath.MonthlyNeeded(g, today) is decimal monthly && g.TargetDate is DateTime date && date >= today)
            {
                var pace = Ui.Text($"Save {Money.Format(monthly)}/month to reach it by {date:MMM yyyy}", muted: true, size: 12);
                pace.Margin = new Thickness(0, 5, 0, 0);
                row.Children.Add(pace);
            }
            GoalList.Children.Add(row);
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
            g.Children.Add(Ui.Chip(t.IsSplit ? t.CategoryLabel : t.IsTransfer ? "Transfer" : Categories.Display(t.Category)).At(1, cell));
            g.Children.Add(Ui.Text(t.Account, muted: true).At(2, cell));
            g.Children.Add(Ui.Text(t.Date.ToString("MMM d", Money.Us), muted: true).At(3, cell));
            var amount = Ui.Text(t.IsTransfer ? Money.Format(t.Size) : Money.Signed(t.Amount), weight: FontWeights.SemiBold,
                brushKey: t.IsIncome ? "Brush.Positive" : t.IsTransfer ? "Brush.TextMuted" : "Brush.Text").At(4, cell);
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

    private void Import_Click(object sender, RoutedEventArgs e) => ImportDialog.Run();

    private void GoTransactions_Click(object sender, RoutedEventArgs e)
    {
        MonthSwitcher.Current = Budgeting.MonthStart(DateTime.Today);
        MainWindow.Instance?.GoTo("transactions");
    }

    private void GoBills_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.GoTo("bills");

    private void GoGoals_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.GoTo("goals");

    private void GoBudgets_Click(object sender, RoutedEventArgs e)
    {
        MonthSwitcher.Current = Budgeting.MonthStart(DateTime.Today);
        MainWindow.Instance?.GoTo("budgets");
    }
}
