using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>
/// The Budgets page: a monthly amount per category, with progress from that month's spending, rollover,
/// one-month amounts, templates, "copy last month" and what's left to assign.
/// </summary>
public partial class BudgetsView : UserControl
{
    public BudgetsView()
    {
        InitializeComponent();
        Month.MonthChanged += (_, _) => Refresh();
        Loaded += (_, _) => { DataStore.Changed += OnDataChanged; Refresh(); };
        Unloaded += (_, _) => DataStore.Changed -= OnDataChanged;
        Refresh();
    }

    private void OnDataChanged(object? sender, EventArgs e) => Refresh();

    public void Refresh()
    {
        var month = MonthSwitcher.Current;
        var summary = Budgeting.Summarize(month);
        bool any = summary.Budgets.Count > 0;

        LeftText.Text = any ? Money.Format(summary.LeftToSpend) : "—";
        LeftBarHost.Children.Clear();
        decimal available = summary.Budgets.Sum(b => b.Available);
        if (any)
        {
            double used = available <= 0 ? 1 : (double)(summary.SpentInBudgets / available);
            LeftBarHost.Children.Add(Ui.ProgressBar(used, summary.LeftToSpend < 0 ? "Brush.Danger" : "Brush.Mint", "Brush.HeroTrack", 8));
        }
        BudgetedText.Text = Money.Format(summary.Budgeted, cents: false);
        decimal carry = summary.Budgets.Sum(b => b.Carry);
        CarryText.Text = carry == 0 ? "" : carry > 0 ? $"+ {Money.Format(carry)} rolled over" : $"{Money.Format(carry)} overspent last month";
        SpentText.Text = Money.Format(summary.SpentInBudgets);
        OverText.Text = summary.OverCount == 0 ? (any ? "None" : "—") : summary.OverCount == 1 ? "1 budget" : $"{summary.OverCount} budgets";
        OverText.SetResourceReference(TextBlock.ForegroundProperty, summary.OverCount > 0 ? "Brush.Danger" : "Brush.Text");

        // Zero-based view: income minus what's budgeted.
        decimal income = summary.Income;
        if (income > 0 && any)
        {
            decimal toAssign = income - summary.Budgeted;
            AssignText.Text = toAssign >= 0
                ? $"{Money.Format(income, cents: false)} income · {Money.Format(summary.Budgeted, cents: false)} budgeted · {Money.Format(toAssign, cents: false)} left to assign"
                : $"{Money.Format(income, cents: false)} income · budgeted {Money.Format(-toAssign, cents: false)} more than you earned";
            AssignText.SetResourceReference(TextBlock.ForegroundProperty, toAssign >= 0 ? "Brush.TextMuted" : "Brush.Warn");
        }
        else
        {
            AssignText.Text = "Budgets repeat every month. Spending in a category counts against its budget.";
            AssignText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        }

        Rows.Children.Clear();
        bool first = true;
        foreach (var status in summary.Budgets.OrderBy(b => Categories.Display(b.Budget.Category), StringComparer.OrdinalIgnoreCase))
        {
            Rows.Children.Add(BuildRow(status, month, first));
            first = false;
        }
        EmptyHost.Content = any ? null
            : Ui.EmptyState("", "No budgets yet",
                "Set a monthly amount for the categories you want to keep an eye on, or let a template (like 50/30/20) set them up for you.",
                "Use a template", Template_Click);
        NewButton.IsEnabled = summary.Budgets.Count < Categories.Spending.Count;

        var previous = month.AddMonths(-1);
        int differences = DataStore.Budgets.Items.Count(b => Budgeting.LimitFor(b, previous) != Budgeting.LimitFor(b, month));
        CopyButton.IsEnabled = differences > 0;
        CopyButton.ToolTip = differences > 0
            ? $"Use {Money.MonthName(previous)}'s amounts for {Money.MonthName(month)} ({differences} budget{(differences == 1 ? "" : "s")} differ)."
            : "This month already uses the same amounts as last month.";

        BuildUnbudgeted(summary);
    }

    private Button BuildRow(BudgetStatus s, DateTime month, bool first)
    {
        var panel = new StackPanel { Margin = new Thickness(10, 14, 10, 14) };

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var left = Ui.Text(s.IsOver ? $"{Money.Format(-s.Left)} over" : $"{Money.Format(s.Left)} left",
            weight: FontWeights.SemiBold, brushKey: s.IsOver ? "Brush.Danger" : s.BrushKey == "Brush.Warn" ? "Brush.Warn" : "Brush.TextMuted");
        DockPanel.SetDock(left, Dock.Right);
        top.Children.Add(left);

        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        name.Inlines.Add(new System.Windows.Documents.Run(Categories.Display(s.Budget.Category)) { FontWeight = FontWeights.SemiBold, FontSize = 15 });
        var detail = new System.Windows.Documents.Run($"   {Money.Format(s.Spent)} of {Money.Format(s.Available, cents: false)}");
        detail.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.TextMuted");
        name.Inlines.Add(detail);
        top.Children.Add(name);

        var badges = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (s.Budget.Rollover)
            badges.Children.Add(PageKit.Pill(s.Carry == 0 ? "Rolls over" : s.Carry > 0 ? $"+{Money.Format(s.Carry, cents: false)} rolled over" : $"{Money.Format(s.Carry, cents: false)} carried", "Brush.Accent")
                .Also(p => p.Margin = new Thickness(0, 0, 6, 0)));
        if (s.Budget.Overrides.ContainsKey(Budgeting.Key(month)))
            badges.Children.Add(PageKit.Pill("This month only", "Brush.TextMuted"));
        top.Children.Add(badges);
        panel.Children.Add(top);

        panel.Children.Add(Ui.ProgressBar(s.Fraction, s.BrushKey));

        return PageKit.Row(panel, $"{Categories.Display(s.Budget.Category)}: {Money.Format(s.Spent)} spent of {Money.Format(s.Available)}",
            () => BudgetDialog.Edit(s.Budget, month: month), first);
    }

    /// <summary>Lists categories with spending this month but no budget (directly or through their parent), each with "Budget it".</summary>
    private void BuildUnbudgeted(MonthSummary summary)
    {
        var budgeted = summary.Budgets.Select(b => b.Budget.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = summary.SpentByCategory
            .Where(kv => kv.Value > 0 && !budgeted.Contains(kv.Key) && !budgeted.Contains(Categories.ParentOf(kv.Key)))
            .OrderByDescending(kv => kv.Value)
            .ToList();

        UnbudgetedRows.Children.Clear();
        UnbudgetedCard.Visibility = groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnbudgetedIntro.Text = $"{Money.Format(summary.Unbudgeted)} spent in {Money.MonthName(summary.Month)} isn't covered by a budget.";

        foreach (var (category, spent) in groups)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            if (Categories.Spending.Contains(category, StringComparer.OrdinalIgnoreCase))
            {
                var link = PageKit.Link("Budget it", () => BudgetDialog.Edit(null, category, summary.Month));
                DockPanel.SetDock(link, Dock.Right);
                row.Children.Add(link);
            }
            var amount = Ui.Text(Money.Format(spent), muted: true);
            amount.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(amount, Dock.Right);
            row.Children.Add(amount);
            row.Children.Add(Ui.Text(Categories.Display(category)));
            UnbudgetedRows.Children.Add(row);
        }
    }

    private void New_Click(object sender, RoutedEventArgs e) => BudgetDialog.Edit(null, month: MonthSwitcher.Current);

    private void Template_Click(object sender, RoutedEventArgs e)
    {
        if (new BudgetTemplateDialog().ShowDialog() == true) Toaster.Show("Budgets created. Click any budget to fine-tune it.");
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var month = MonthSwitcher.Current;
        int n = CopyLastMonth(month);
        Toaster.Show(n == 1 ? $"Copied 1 amount from {Money.MonthName(month.AddMonths(-1))}." : $"Copied {n} amounts from {Money.MonthName(month.AddMonths(-1))}.");
    }

    /// <summary>Makes each budget's amount for <paramref name="month"/> match the month before. Returns how many changed.</summary>
    public static int CopyLastMonth(DateTime month)
    {
        month = Budgeting.MonthStart(month);
        var previous = month.AddMonths(-1);
        var changed = new List<Budget>();
        foreach (var b in DataStore.Budgets.Items)
        {
            decimal last = Budgeting.LimitFor(b, previous);
            if (last == Budgeting.LimitFor(b, month)) continue;
            if (last == b.MonthlyLimit) b.Overrides.Remove(Budgeting.Key(month));
            else b.Overrides[Budgeting.Key(month)] = last;
            changed.Add(b);
        }
        if (changed.Count > 0) DataStore.Budgets.UpdateMany(changed);
        return changed.Count;
    }
}
