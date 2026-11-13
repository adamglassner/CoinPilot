using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>The Debts page: what you owe, payments, and a snowball / avalanche payoff plan.</summary>
public sealed class DebtsView : DataPage
{
    protected override void Build()
    {
        var today = DateTime.Today;
        var debts = DataStore.Debts.Items;
        var active = debts.Where(d => !d.IsPaidOff).ToList();
        var settings = DataStore.DebtPlan;
        var plan = DebtPlanner.CurrentPlan();

        Add(PageKit.Header("", "Debts", "See what you owe and the fastest way to pay it off."));
        Add(PageKit.ActionBar("Record payments here to lower each balance.",
            PageKit.Primary("", "Add debt", (_, _) => DebtDialog.Edit(null))));

        string freeDate = active.Count == 0 ? (debts.Count > 0 ? "Debt-free!" : "—")
            : plan.NeverPaysOff ? "Not at this pace"
            : plan.DebtFreeDate(today)!.Value.ToString("MMM yyyy", Money.Us);
        Add(PageKit.KpiRow(
            PageKit.Kpi("Total owed", Money.Format(active.Sum(d => d.Balance)), active.Count == 1 ? "1 debt" : $"{active.Count} debts", hero: true),
            PageKit.Kpi("Monthly minimums", Money.Format(active.Sum(d => d.MinimumPayment))),
            PageKit.Kpi("Debt-free", freeDate, active.Count == 0 || plan.NeverPaysOff ? null : MonthsText(plan.Months),
                valueBrush: plan.NeverPaysOff && active.Count > 0 ? "Brush.Danger" : null),
            PageKit.Kpi("Interest still to pay", active.Count == 0 || plan.NeverPaysOff ? "—" : Money.Format(plan.TotalInterest), "With this plan")));

        if (debts.Count == 0)
        {
            Add(PageKit.Card(Ui.EmptyState("", "No debts added",
                "Add credit cards, student loans, car loans and other debts. CoinPilot shows how long they'll take to pay off and the order that saves you the most.",
                "Add debt", (_, _) => DebtDialog.Edit(null))));
            return;
        }

        if (active.Count > 0) Add(BuildPlanCard(settings, plan, active, today));

        // ----- Debts, in plan order -----
        var list = new StackPanel();
        bool first = true;
        foreach (var d in active.OrderBy(d => plan.Order.GetValueOrDefault(d.Id, int.MaxValue)))
        {
            list.Children.Add(BuildRow(d, plan, today, first));
            first = false;
        }
        var paid = debts.Where(d => d.IsPaidOff).ToList();
        if (paid.Count > 0)
        {
            list.Children.Add(PageKit.Section("Paid off"));
            foreach (var d in paid) list.Children.Add(BuildRow(d, plan, today, false));
        }
        Add(PageKit.Card(list, padding: new Thickness(14, 8, 14, 8)));
    }

    private static string MonthsText(int months) =>
        months < 12 ? (months == 1 ? "In 1 month" : $"In {months} months")
        : months % 12 == 0 ? $"In {months / 12} year{(months / 12 == 1 ? "" : "s")}"
        : $"In {months / 12} yr {months % 12} mo";

    private Border BuildPlanCard(DebtPlanSettings settings, DebtPlanResult plan, List<Debt> active, DateTime today)
    {
        var panel = new StackPanel();

        var choose = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var extraBox = new TextBox
        {
            Text = settings.ExtraPayment > 0 ? settings.ExtraPayment.ToString("0.##", Money.Us) : "",
            Width = 110,
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Automation.AutomationProperties.SetName(extraBox, "Extra payment each month");
        void ApplyExtra()
        {
            if (string.IsNullOrWhiteSpace(extraBox.Text)) { if (settings.ExtraPayment != 0) { settings.ExtraPayment = 0; settings.Save(); } return; }
            if (Money.TryParse(extraBox.Text, out var extra)) { if (extra != settings.ExtraPayment) { settings.ExtraPayment = extra; settings.Save(); } }
            else Toaster.Show("Enter the extra payment as an amount, like 100.");
        }
        extraBox.LostKeyboardFocus += (_, _) => ApplyExtra();
        extraBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) ApplyExtra(); };

        var extraPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        extraPanel.Children.Add(Ui.Text("Extra each month  $", muted: true));
        extraPanel.Children.Add(extraBox);
        extraPanel.Children.Add(PageKit.Link("Apply", ApplyExtra));
        DockPanel.SetDock(extraPanel, Dock.Right);
        choose.Children.Add(extraPanel);
        choose.Children.Add(PageKit.ChoiceChips(
            [DebtPlanner.Avalanche, DebtPlanner.Snowball], settings.Strategy,
            pick => { settings.Strategy = pick; settings.Save(); }));
        panel.Children.Add(choose);

        var explain = new TextBlock { Style = Res("Muted"), Margin = new Thickness(0, 0, 0, 10) };
        explain.Text = settings.Strategy == DebtPlanner.Snowball
            ? "Snowball: pay the minimum on everything and put the rest toward the smallest balance first. Quick wins keep you going."
            : "Avalanche: pay the minimum on everything and put the rest toward the highest interest rate first. You pay the least interest.";
        panel.Children.Add(explain);

        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14.5, LineHeight = 21 };
        if (plan.NeverPaysOff)
        {
            summary.Text = $"Paying {Money.Format(plan.MonthlyPayment)} a month doesn't keep up with the interest on at least one debt. Add an extra monthly amount or raise a minimum payment.";
            summary.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        }
        else
        {
            summary.Inlines.Add($"Paying {Money.Format(plan.MonthlyPayment)} a month, you'll be debt-free by ");
            summary.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run(plan.DebtFreeDate(today)!.Value.ToString("MMMM yyyy", Money.Us))));
            summary.Inlines.Add($" and pay {Money.Format(plan.TotalInterest)} in interest.");

            // Compare with the other strategy.
            string other = settings.Strategy == DebtPlanner.Snowball ? DebtPlanner.Avalanche : DebtPlanner.Snowball;
            var alt = DebtPlanner.Plan(active, other, settings.ExtraPayment);
            if (!alt.NeverPaysOff && active.Count > 1)
            {
                decimal diff = alt.TotalInterest - plan.TotalInterest;
                string compare = Math.Abs(diff) < 1 ? $" {other} would cost about the same."
                    : diff > 0 ? $" That's {Money.Format(diff)} less interest than {other.ToLowerInvariant()}."
                    : $" {other} would save {Money.Format(-diff)} in interest.";
                summary.Inlines.Add(compare);
            }
        }
        panel.Children.Add(summary);
        return PageKit.Card(panel, "Payoff plan");
    }

    private static Button BuildRow(Debt d, DebtPlanResult plan, DateTime today, bool firstRow)
    {
        var grid = new Grid { Margin = new Thickness(10, 14, 10, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Plan order badge
        var badge = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
        badge.SetResourceReference(Border.BackgroundProperty, d.IsPaidOff ? "Brush.SurfaceAlt" : "Brush.AccentSoft");
        var badgeText = Ui.Text(d.IsPaidOff ? "✓" : plan.Order.GetValueOrDefault(d.Id).ToString(), weight: FontWeights.Bold, brushKey: "Brush.Accent");
        badgeText.HorizontalAlignment = HorizontalAlignment.Center;
        badge.Child = badgeText;
        badge.ToolTip = d.IsPaidOff ? "Paid off" : "Order in your payoff plan";
        grid.Children.Add(badge);

        var middle = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
        Grid.SetColumn(middle, 1);
        var title = new TextBlock();
        title.Inlines.Add(new System.Windows.Documents.Run(d.Name) { FontWeight = FontWeights.SemiBold, FontSize = 15 });
        var details = new System.Windows.Documents.Run(
            $"   {d.Type} · {Money.Percent(d.InterestRate)} APR · {Money.Format(d.MinimumPayment)} minimum");
        details.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.TextMuted");
        title.Inlines.Add(details);
        middle.Children.Add(title);

        double fraction = d.OriginalBalance <= 0 ? 1 : (double)(d.PaidOff / d.OriginalBalance);
        var bar = Ui.ProgressBar(d.IsPaidOff ? 1 : fraction, "Brush.Accent", height: 8);
        bar.Margin = new Thickness(0, 9, 0, 6);
        middle.Children.Add(bar);

        string when = d.IsPaidOff ? (d.Payments.Count > 0 ? $"Paid off {d.Payments[^1].Date:MMM d, yyyy}" : "Paid off")
            : plan.PayoffMonth.TryGetValue(d.Id, out int m) ? $"Paid off by {new DateTime(today.Year, today.Month, 1).AddMonths(m):MMM yyyy} with your plan"
            : "Won't be paid off at this pace";
        middle.Children.Add(Ui.Text($"{Money.Format(d.PaidOff)} paid of {Money.Format(d.OriginalBalance)} · {when}", muted: true, size: 12.5));
        grid.Children.Add(middle);

        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(right, 2);
        var balance = Ui.Text(Money.Format(d.Balance), size: 17, weight: FontWeights.Bold);
        balance.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(balance);
        if (!d.IsPaidOff)
        {
            var pay = PageKit.Link("Record payment", () => DebtPaymentDialog.Record(d));
            pay.HorizontalAlignment = HorizontalAlignment.Right;
            pay.Margin = new Thickness(0, 2, -8, 0);
            right.Children.Add(pay);
        }
        grid.Children.Add(right);

        return PageKit.Row(grid, $"{d.Name}, {Money.Format(d.Balance)} owed", () => DebtDialog.Edit(d), firstRow);
    }
}
