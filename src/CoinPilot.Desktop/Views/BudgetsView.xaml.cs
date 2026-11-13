using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>The Budgets page: a monthly limit per category, with progress from that month's spending.</summary>
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
        var summary = Budgeting.Summarize(MonthSwitcher.Current);
        bool any = summary.Budgets.Count > 0;

        LeftText.Text = any ? Money.Format(summary.LeftToSpend) : "—";
        LeftBarHost.Children.Clear();
        if (any)
        {
            double used = summary.Budgeted <= 0 ? 0 : (double)(summary.SpentInBudgets / summary.Budgeted);
            LeftBarHost.Children.Add(Ui.ProgressBar(used, summary.LeftToSpend < 0 ? "Brush.Danger" : "Brush.Mint", "Brush.HeroTrack", 8));
        }
        BudgetedText.Text = Money.Format(summary.Budgeted, cents: false);
        SpentText.Text = Money.Format(summary.SpentInBudgets);
        OverText.Text = summary.OverCount == 0 ? (any ? "None" : "—") : summary.OverCount == 1 ? "1 budget" : $"{summary.OverCount} budgets";
        OverText.SetResourceReference(TextBlock.ForegroundProperty, summary.OverCount > 0 ? "Brush.Danger" : "Brush.Text");

        Rows.Children.Clear();
        bool first = true;
        foreach (var status in summary.Budgets)
        {
            Rows.Children.Add(BuildRow(status, first));
            first = false;
        }
        EmptyHost.Content = any ? null
            : Ui.EmptyState("", "No budgets yet",
                "Set a monthly amount for the categories you want to keep an eye on, like groceries or dining out.",
                "Create your first budget", New_Click);
        NewButton.IsEnabled = summary.Budgets.Count < Categories.Spending.Length;

        BuildUnbudgeted(summary);
    }

    private Button BuildRow(BudgetStatus s, bool first)
    {
        var panel = new StackPanel { Margin = new Thickness(10, 14, 10, 14) };

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var left = Ui.Text(s.IsOver ? $"{Money.Format(-s.Left)} over" : $"{Money.Format(s.Left)} left",
            weight: FontWeights.SemiBold, brushKey: s.IsOver ? "Brush.Danger" : s.BrushKey == "Brush.Warn" ? "Brush.Warn" : "Brush.TextMuted");
        DockPanel.SetDock(left, Dock.Right);
        top.Children.Add(left);
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        name.Inlines.Add(new System.Windows.Documents.Run(s.Budget.Category) { FontWeight = FontWeights.SemiBold, FontSize = 15 });
        var detail = new System.Windows.Documents.Run($"   {Money.Format(s.Spent)} of {Money.Format(s.Limit, cents: false)}");
        detail.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.TextMuted");
        name.Inlines.Add(detail);
        top.Children.Add(name);
        panel.Children.Add(top);

        panel.Children.Add(Ui.ProgressBar(s.Fraction, s.BrushKey));

        var row = new Button { Content = panel, Style = (Style)FindResource("RowButton"), ToolTip = "Edit or delete this budget" };
        if (first) row.BorderThickness = new Thickness(0);
        System.Windows.Automation.AutomationProperties.SetName(row,
            $"{s.Budget.Category}: {Money.Format(s.Spent)} spent of {Money.Format(s.Limit)}");
        row.Click += (_, _) => BudgetDialog.Edit(s.Budget);
        return row;
    }

    /// <summary>Lists categories with spending this month but no budget, each with a "Budget it" link.</summary>
    private void BuildUnbudgeted(MonthSummary summary)
    {
        var budgeted = summary.Budgets.Select(b => b.Budget.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = summary.Transactions
            .Where(t => t.Amount < 0 && !budgeted.Contains(t.Category))
            .GroupBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Category: g.First().Category, Spent: g.Sum(t => -t.Amount)))
            .OrderByDescending(x => x.Spent)
            .ToList();

        UnbudgetedRows.Children.Clear();
        UnbudgetedCard.Visibility = groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnbudgetedIntro.Text = $"{Money.Format(summary.Unbudgeted)} spent in {Money.MonthName(summary.Month)} isn't covered by a budget.";

        foreach (var (category, spent) in groups)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            bool canBudget = Categories.Spending.Contains(category, StringComparer.OrdinalIgnoreCase);
            if (canBudget)
            {
                var link = new Button { Content = "Budget it", Style = (Style)FindResource("LinkButton") };
                link.Click += (_, _) => BudgetDialog.Edit(null, category);
                DockPanel.SetDock(link, Dock.Right);
                row.Children.Add(link);
            }
            var amount = Ui.Text(Money.Format(spent), muted: true);
            amount.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(amount, Dock.Right);
            row.Children.Add(amount);
            row.Children.Add(Ui.Text(category));
            UnbudgetedRows.Children.Add(row);
        }
    }

    private void New_Click(object sender, RoutedEventArgs e) => BudgetDialog.Edit(null);
}
