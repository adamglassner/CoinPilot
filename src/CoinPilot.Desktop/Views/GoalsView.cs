using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>The Goals page: savings goals with progress rings, milestones, linked accounts and the monthly amount to stay on track.</summary>
public sealed class GoalsView : DataPage
{
    private static readonly int[] Milestones = [25, 50, 75, 100];

    protected override void Build()
    {
        GoalMath.RefreshLinked();
        var today = DateTime.Today;
        var goals = DataStore.Goals.Items;
        var active = goals.Where(g => !g.IsReached).ToList();
        decimal monthly = active.Sum(g => GoalMath.MonthlyNeeded(g, today) ?? 0);

        Add(PageKit.Header("", "Goals", "Save toward the things that matter."));
        Add(PageKit.ActionBar("Click a goal to edit it. Use Add money as you save, or link a goal to a savings account.",
            PageKit.Primary("", "New goal", (_, _) => GoalDialog.Edit(null))));

        Add(PageKit.KpiRow(
            PageKit.Kpi("Saved so far", Money.Format(goals.Sum(g => g.Saved)),
                $"of {Money.Format(goals.Sum(g => g.Target), cents: false)} across {goals.Count} goal{(goals.Count == 1 ? "" : "s")}", hero: true),
            PageKit.Kpi("Still to save", Money.Format(goals.Sum(g => g.Remaining))),
            PageKit.Kpi("To stay on track", Money.Format(monthly) + "/mo", "For goals with a target date"),
            PageKit.Kpi("Goals reached", goals.Count(g => g.IsReached).ToString())));

        if (goals.Count == 0)
        {
            Add(PageKit.Card(Ui.EmptyState("", "No goals yet",
                "Create a goal like an emergency fund, a vacation or a down payment. Add a target date and CoinPilot works out what to save each month.",
                "New goal", (_, _) => GoalDialog.Edit(null))));
            return;
        }

        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -16, 0) };
        foreach (var g in active.OrderBy(g => g.TargetDate ?? DateTime.MaxValue).ThenBy(g => g.Name))
            grid.Children.Add(BuildCard(g, today));
        foreach (var g in goals.Where(g => g.IsReached).OrderBy(g => g.Name))
            grid.Children.Add(BuildCard(g, today));
        Add(grid);
    }

    private Button BuildCard(Goal g, DateTime today)
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var ring = Charts.Ring(g.Fraction, 84, 9, g.IsReached ? "Brush.Positive" : "Brush.Accent");
        ring.Margin = new Thickness(0, 0, 18, 0);
        ring.VerticalAlignment = VerticalAlignment.Top;
        layout.Children.Add(ring);

        var panel = new StackPanel().At(1);
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        top.Children.Add(new TextBlock { Text = g.Name, Style = Res("CardTitle"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 2), VerticalAlignment = VerticalAlignment.Center });
        if (g.IsReached || g.IsLinked)
        {
            var pill = g.IsReached ? PageKit.Pill("Reached", "Brush.Positive") : PageKit.Pill("Linked: " + g.LinkedAccount, "Brush.Accent");
            pill.VerticalAlignment = VerticalAlignment.Center;
            pill.Margin = new Thickness(0, 0, 0, 2);
            top.Children.Add(pill);
        }
        panel.Children.Add(top);

        var amounts = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
        amounts.Inlines.Add(new System.Windows.Documents.Run(Money.Format(g.Saved)) { FontSize = 22, FontWeight = FontWeights.Bold });
        var of = new System.Windows.Documents.Run($"  of {Money.Format(g.Target, cents: false)}");
        of.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.TextMuted");
        amounts.Inlines.Add(of);
        panel.Children.Add(amounts);

        // Milestones: 25 / 50 / 75 / 100%
        var stones = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (int m in Milestones)
        {
            bool done = g.Fraction * 100 >= m - 0.0001;
            var dot = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(0, 0, 6, 4) };
            dot.SetResourceReference(Border.BackgroundProperty, done ? "Brush.AccentSoft" : "Brush.SurfaceAlt");
            dot.Child = Ui.Text((done ? "✓ " : "") + m + "%", size: 11.5, weight: done ? FontWeights.SemiBold : FontWeights.Normal,
                brushKey: done ? "Brush.Accent" : "Brush.TextMuted");
            dot.ToolTip = done ? $"{m}% milestone reached" : $"{m}% milestone: {Money.Format(g.Target * m / 100, cents: false)}";
            stones.Children.Add(dot);
        }
        panel.Children.Add(stones);

        string pace;
        string paceBrush = "Brush.TextMuted";
        if (g.IsReached) pace = "Goal reached. Nice work!";
        else if (g.TargetDate is not DateTime date) pace = $"{Money.Format(g.Remaining)} to go · no target date";
        else if (date.Date < today)
        {
            pace = $"Target date ({date:MMM d, yyyy}) has passed · {Money.Format(g.Remaining)} to go";
            paceBrush = "Brush.Warn";
        }
        else pace = $"By {date:MMM yyyy} · save {Money.Format(GoalMath.MonthlyNeeded(g, today) ?? 0)}/month";

        var bottom = new DockPanel();
        if (!g.IsLinked)
        {
            var add = PageKit.Link(g.IsReached ? "Add or take out" : "Add money", () => GoalMoneyDialog.Open(g));
            DockPanel.SetDock(add, Dock.Right);
            bottom.Children.Add(add);
        }
        var paceText = Ui.Text(pace, size: 12.5, brushKey: paceBrush);
        paceText.TextWrapping = TextWrapping.Wrap;
        paceText.TextTrimming = TextTrimming.None;
        bottom.Children.Add(paceText);
        panel.Children.Add(bottom);
        layout.Children.Add(panel);

        var card = new Border { Style = Res("Card"), Child = layout };
        var button = new Button { Content = card, Style = Res("CardButton"), Margin = new Thickness(0, 0, 16, 16), ToolTip = "Edit or delete" };
        System.Windows.Automation.AutomationProperties.SetName(button, $"{g.Name}: {Money.Format(g.Saved)} of {Money.Format(g.Target)}");
        button.Click += (_, _) => GoalDialog.Edit(g);
        return button;
    }
}
