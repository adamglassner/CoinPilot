using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>Pieces for spending alerts, the paycheck planner and this year vs last.</summary>
public static class InsightViews
{
    /// <summary>"Heads up" on the Dashboard: alerts with Dismiss, and Update the bill / Open forecast where they help. Null when there are none.</summary>
    public static Border? AlertsCard()
    {
        var alerts = Insights.Alerts(DateTime.Today);
        if (alerts.Count == 0) return null;
        var panel = new StackPanel();
        foreach (var a in alerts.Take(4))
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            var dismiss = PageKit.Link("Dismiss", () => Insights.Dismiss(a));
            dismiss.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(dismiss, Dock.Right);
            row.Children.Add(dismiss);
            if (a.Kind == "bill-price")
            {
                var update = PageKit.Link($"Update the bill to {Money.Format(a.NewAmount ?? 0)}", () => { Insights.ApplyBillPrice(a); Toaster.Show("Bill updated."); });
                update.Margin = new Thickness(12, 0, 0, 0);
                DockPanel.SetDock(update, Dock.Right);
                row.Children.Add(update);
            }
            else if (a.Kind == "dip")
            {
                var open = PageKit.Link("Open forecast", () => MainWindow.Instance?.GoTo("forecast"));
                open.Margin = new Thickness(12, 0, 0, 0);
                DockPanel.SetDock(open, Dock.Right);
                row.Children.Add(open);
            }
            string brush = a.Kind is "over" or "dip" ? "Brush.Danger" : "Brush.Warn";
            string glyph = a.Kind switch { "over" => "", "budget" => "", "bill-price" => "", "dip" => "", _ => "" };
            var icon = new TextBlock { Text = glyph, Style = (Style)Application.Current.FindResource("Icon"), FontSize = 16, Margin = new Thickness(0, 2, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            icon.SetResourceReference(TextBlock.ForegroundProperty, brush);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            var words = new StackPanel();
            words.Children.Add(Ui.Text(a.Title, weight: FontWeights.SemiBold));
            words.Children.Add(Ui.Text(a.Body, muted: true, size: 12.5).Also(t => t.TextWrapping = TextWrapping.Wrap));
            row.Children.Add(words);
            panel.Children.Add(row);
        }
        if (alerts.Count > 4) panel.Children.Add(Ui.Text($"+ {alerts.Count - 4} more", muted: true, size: 12.5).Also(t => t.Margin = new Thickness(28, 4, 0, 0)));
        return PageKit.Card(panel, "Heads up");
    }

    /// <summary>The Income page's "Plan your next paycheck" card.</summary>
    public static Border PlanCard(IncomeSource next)
    {
        var plan = Insights.PlanPaycheck(next, next.NextDate);
        var row = new DockPanel();
        var open = PageKit.Primary("", "Plan this paycheck", (_, _) => PaycheckPlanDialog.Open(next));
        DockPanel.SetDock(open, Dock.Right);
        row.Children.Add(open);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Ui.Text($"Plan your next paycheck: {next.Name}, {next.NextDate:MMM d}", weight: FontWeights.SemiBold, size: 15));
        words.Children.Add(plan.Leftover >= 0
            ? Ui.Text($"{Money.Format(plan.Leftover, cents: false)} left over after bills and budgets until {plan.Next:MMM d}.", muted: true)
            : Ui.Text($"{Money.Format(-plan.Leftover, cents: false)} short of what's due before {plan.Next:MMM d}.", brushKey: "Brush.Danger"));
        row.Children.Add(words);
        return PageKit.Card(row, padding: new Thickness(22, 16, 22, 16));
    }

    /// <summary>The Reports page's "Compared with last year": this budget month so far against the same days a year ago.</summary>
    public static Border YearOverYearCard()
    {
        var today = DateTime.Today;
        var from = Budgeting.MonthStart(today);
        var y = Insights.Compare(from, today);
        string title = $"Compared with last year · {from:MMM d} – {today:MMM d}";
        if (!y.HasLastYear)
            return PageKit.Card(Ui.Text($"Once you have a year of history, this shows {from:MMM d} – {today:MMM d} against the same days last year.", muted: true)
                .Also(t => t.TextWrapping = TextWrapping.Wrap), title);
        TextBlock Change(decimal now, decimal before, bool spending = true)
        {
            var c = YearOverYear.Change(now, before);
            if (c is not int v) return Ui.Text("new", muted: true, size: 12.5);
            bool good = spending ? v <= 0 : v >= 0;
            return Ui.Text($"{(v > 0 ? "+" : "")}{v}%", size: 12.5, weight: FontWeights.SemiBold, brushKey: v == 0 ? "Brush.TextMuted" : good ? "Brush.Positive" : "Brush.Danger");
        }
        var panel = new StackPanel();
        var kpis = Ui.Columns(1, 1);
        StackPanel Kpi(string label, decimal now, decimal before, bool spending)
        {
            var s = new StackPanel();
            s.Children.Add(Ui.Text(label, muted: true));
            s.Children.Add(Ui.Text(Money.Format(now, cents: false), size: 22, weight: FontWeights.Bold));
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(Ui.Text($"{Money.Format(before, cents: false)} last year  ", muted: true, size: 12.5));
            line.Children.Add(Change(now, before, spending));
            s.Children.Add(line);
            return s;
        }
        kpis.Children.Add(Kpi("Spent", y.Spent, y.SpentBefore, true).At(0));
        kpis.Children.Add(Kpi("Income", y.Income, y.IncomeBefore, false).At(1));
        panel.Children.Add(kpis);
        foreach (var (cat, now, before) in y.Categories.Take(6))
        {
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(Ui.Text($"{Money.Format(before, cents: false)} → {Money.Format(now, cents: false)}   ", muted: true));
            right.Children.Add(Change(now, before).Also(t => { t.Width = 48; t.TextAlignment = TextAlignment.Right; }));
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            row.Children.Add(Ui.Text(cat, weight: FontWeights.SemiBold));
            panel.Children.Add(row);
        }
        return PageKit.Card(panel, title);
    }
}

/// <summary>How a paycheck could be split until the next one: bills, budgets, debts, savings, and what's left over.</summary>
public sealed class PaycheckPlanDialog : DialogShell
{
    public PaycheckPlanDialog(IncomeSource s, DateTime payday)
        : base("Plan this paycheck", "", allowDelete: false, saveText: "Done", width: 560)
    {
        var plan = Insights.PlanPaycheck(s, payday);
        HideCancel();
        bool shortBy = plan.Leftover < 0;
        var head = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 12) };
        head.SetResourceReference(Border.BackgroundProperty, shortBy ? "Brush.SurfaceAlt" : "Brush.AccentSoft");
        var hs = new StackPanel();
        hs.Children.Add(Ui.Text($"{s.Name} on {plan.Payday:MMM d}: {Money.Format(plan.Amount)}. Until the next one on {plan.Next:MMM d}:", muted: true).Also(t => t.TextWrapping = TextWrapping.Wrap));
        hs.Children.Add(Ui.Text(shortBy ? "Short by" : "Left over", muted: true).Also(t => t.Margin = new Thickness(0, 8, 0, 0)));
        hs.Children.Add(Ui.Text(Money.Format(Math.Abs(plan.Leftover)), size: 28, weight: FontWeights.Bold, brushKey: shortBy ? "Brush.Danger" : "Brush.Positive"));
        hs.Children.Add(Ui.Text(shortBy
            ? "This paycheck doesn't cover everything before the next one. Spending a little less from your budgets, or moving a bill, closes the gap."
            : plan.Leftover > 0 ? "Ideas: put it toward a goal, pay extra on a debt, or keep it as a cushion." : "Every dollar has a job.").Also(t => t.TextWrapping = TextWrapping.Wrap));
        head.Child = hs;
        Body.Children.Add(head);

        void Line(string label, string sub, decimal amount)
        {
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
            row.Children.Add(Ui.Text(Money.Format(amount), weight: FontWeights.SemiBold).Also(t => DockPanel.SetDock(t, Dock.Right)));
            var words = new StackPanel();
            words.Children.Add(Ui.Text(label));
            if (sub.Length > 0) words.Children.Add(Ui.Text(sub, muted: true, size: 12));
            row.Children.Add(words);
            Body.Children.Add(row);
        }
        var bills = plan.Lines.Where(l => l.Kind == "bill").OrderBy(l => l.Date).ToList();
        if (bills.Count > 0)
        {
            Body.Children.Add(PageKit.Section($"Bills due · {Money.Format(plan.Total("bill"), cents: false)}", 6));
            foreach (var b in bills) Line(b.Label, b.Date.ToString("ddd, MMM d"), b.Amount);
        }
        var others = plan.Lines.Where(l => l.Kind != "bill").ToList();
        if (others.Count > 0)
        {
            Body.Children.Add(PageKit.Section("Spending and saving", 12));
            foreach (var o in others)
                Line(o.Kind == "budget" ? "Budgets" : o.Label, o.Kind switch { "budget" => o.Label.Replace("Budgets for", "For the next"), "debt" => "Debt payment", _ => "Savings" }, o.Amount);
        }
        if (plan.Lines.Count == 0) Body.Children.Add(Ui.Text("Nothing is planned yet. Add your bills and budgets to see what each paycheck needs to cover.", muted: true).Also(t => t.TextWrapping = TextWrapping.Wrap));
    }

    protected override string? TrySave() => null;

    public static void Open(IncomeSource s) => new PaycheckPlanDialog(s, s.NextDate) { Owner = MainWindow.Instance }.ShowDialog();
}
