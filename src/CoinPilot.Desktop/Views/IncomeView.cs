using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>The Income page: paychecks and other money that comes in regularly, and what's come in this month.</summary>
public sealed class IncomeView : DataPage
{
    protected override void Build()
    {
        var today = DateTime.Today;
        var sources = DataStore.Income.Items.Where(s => !s.IsDone).OrderBy(s => s.NextDate).ToList();
        var month = Budgeting.MonthStart(today);
        var monthEnd = Budgeting.MonthEnd(month);
        var summary = Budgeting.Summarize(month);
        var stillComing = sources.SelectMany(s => IncomeSchedule.Paydays(s, today, monthEnd).Select(d => (s, d))).ToList();
        decimal comingAmount = stillComing.Sum(x => x.s.Amount > 0 ? x.s.Amount : IncomeSchedule.Typical(x.s));
        var next = sources.FirstOrDefault();
        var asking = Paydays.Asking(today);

        Add(PageKit.Header("", "Income", "Paychecks and other money coming in."));
        Add(PageKit.ActionBar("Same amount every time? It's added on payday. Changes each time? WillowWallet asks how much on payday.",
            PageKit.Ghost("", "Add one-time income", (_, _) => TransactionDialog.AddIncome()),
            PageKit.Primary("", "Add income", (_, _) => IncomeDialog.Edit(null))));

        if (asking.Count > 0)
        {
            var banner = new DockPanel();
            var go = PageKit.Primary("", asking.Count == 1 ? "Enter amount" : "Enter amounts", (_, _) => PaydayDialog.AskNow());
            DockPanel.SetDock(go, Dock.Right);
            banner.Children.Add(go);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(Ui.Text(asking.Count == 1 ? $"Payday: {asking[0].Name}" : $"{asking.Count} paydays need an amount", weight: FontWeights.SemiBold, size: 15));
            words.Children.Add(Ui.Text(asking.Count == 1 ? $"Due {asking[0].NextDate:dddd, MMM d}. Enter what came in and it's added to Transactions." : string.Join(" · ", asking.Select(a => a.Name)), muted: true));
            banner.Children.Add(words);
            Add(PageKit.Card(banner, padding: new Thickness(22, 16, 22, 16)));
        }

        Add(PageKit.KpiRow(
            PageKit.Kpi("Came in this month", Money.Format(summary.Income), Money.MonthName(month), hero: true),
            PageKit.Kpi("Still coming this month", Money.Format(comingAmount), stillComing.Count == 1 ? "1 payday" : $"{stillComing.Count} paydays"),
            PageKit.Kpi("Expected per month", Money.Format(Paydays.ExpectedMonthly()), "Average, all regular income"),
            PageKit.Kpi("Next payday", next == null ? "—" : next.NextDate.Date == today ? "Today" : next.NextDate.ToString("MMM d", Money.Us),
                next?.Name ?? "Add your paycheck")));

        if (sources.Count == 0)
        {
            Add(PageKit.Card(Ui.EmptyState("", "No regular income yet",
                "Add your paycheck and any other money that comes in regularly: a side job, benefits, child support. If it's the same every time, WillowWallet adds it on payday; if it changes, it asks you how much.",
                "Add income", (_, _) => IncomeDialog.Edit(null))));
        }
        else
        {
            var list = new StackPanel();
            for (int i = 0; i < sources.Count; i++) list.Children.Add(Row(sources[i], today, i == 0));
            Add(PageKit.Card(list, padding: new Thickness(14, 8, 14, 8)));
        }

        // Income recorded this month
        var received = summary.Transactions.Where(t => t.IsIncome).OrderByDescending(t => t.Date).Take(8).ToList();
        if (received.Count > 0)
        {
            var panel = new StackPanel();
            foreach (var t in received)
            {
                var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
                var amount = Ui.Text("+" + Money.Format(t.Amount), weight: FontWeights.SemiBold, brushKey: "Brush.Positive");
                DockPanel.SetDock(amount, Dock.Right);
                row.Children.Add(amount);
                var date = Ui.Text(t.Date.ToString("MMM d", Money.Us), muted: true).Also(x => x.Width = 70);
                DockPanel.SetDock(date, Dock.Left);
                row.Children.Add(date);
                row.Children.Add(Ui.Text($"{t.Payee} · {Categories.Display(t.Category)} · {t.Account}"));
                panel.Children.Add(row);
            }
            Add(PageKit.Card(panel, $"Came in during {Money.MonthName(month)}"));
        }
    }

    private static Button Row(IncomeSource s, DateTime today, bool first)
    {
        var g = Ui.Columns(0.2, 1, 0.46, 0.36, 0.52);
        var cell = new Thickness(8, 12, 8, 12);

        // Date tile
        var tile = new Border { Width = 56, Padding = new Thickness(0, 6, 0, 6), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left };
        tile.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        var tileText = new StackPanel();
        tileText.Children.Add(Ui.Text(s.NextDate.ToString("MMM", Money.Us).ToUpperInvariant(), size: 11, weight: FontWeights.SemiBold, brushKey: "Brush.Accent").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center));
        tileText.Children.Add(Ui.Text(s.NextDate.Day.ToString(), size: 19, weight: FontWeights.Bold, brushKey: "Brush.Accent").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center));
        tile.Child = tileText;
        g.Children.Add(tile.At(0, new Thickness(4, 8, 8, 8)));

        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(Ui.Text(s.Name, weight: FontWeights.SemiBold));
        var parts = new List<string> { IncomeSchedule.Describe(s), Categories.Display(s.Category) };
        if (s.Account.Length > 0) parts.Add(s.Account);
        name.Children.Add(Ui.Text(string.Join(" · ", parts), muted: true, size: 12.5));
        g.Children.Add(name.At(1, cell));

        // How it's recorded
        int days = (s.NextDate.Date - today).Days;
        string when = days < 0 ? $"Due {-days} day{(days == -1 ? "" : "s")} ago" : days == 0 ? "Payday today" : days == 1 ? "Tomorrow" : $"In {days} days";
        var how = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        how.Children.Add(Ui.Text(when, weight: FontWeights.SemiBold, size: 12.5, brushKey: days <= 0 ? "Brush.Accent" : null));
        how.Children.Add(Ui.Text(s.Varies ? "Asks how much" : s.AutoAdd ? "Automatic" : "Asks to confirm", muted: true, size: 12));
        g.Children.Add(how.At(2, cell));

        decimal typical = s.Amount > 0 ? s.Amount : IncomeSchedule.Typical(s);
        var amount = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        amount.Children.Add(Ui.Text(s.Varies ? (typical > 0 ? $"≈ {Money.Format(typical)}" : "Varies") : Money.Format(s.Amount), weight: FontWeights.SemiBold, size: 14.5).Also(t => t.HorizontalAlignment = HorizontalAlignment.Right));
        decimal perMonth = IncomeSchedule.MonthlyAmount(s);
        amount.Children.Add(Ui.Text(perMonth > 0 ? $"{Money.Format(perMonth, cents: false)}/mo" : s.Repeats == IncomeSchedule.Once ? "once" : "per payday", muted: true, size: 12).Also(t => t.HorizontalAlignment = HorizontalAlignment.Right));
        g.Children.Add(amount.At(3, cell));

        var action = days <= 0 && s.NeedsAsking
            ? PageKit.Link(s.Varies ? "Enter amount" : "Confirm", () => PaydayDialog.AskNow())
            : PageKit.Link("Received", () => ReceiveNow(s));
        action.HorizontalAlignment = HorizontalAlignment.Right;
        g.Children.Add(action.At(4, cell));

        return PageKit.Row(g, s.Name, () => IncomeDialog.Edit(s), firstRow: first);
    }

    /// <summary>Records the next payday now (it came early), asking for the amount when it changes.</summary>
    private static void ReceiveNow(IncomeSource s)
    {
        if (s.Varies || !s.AutoAdd)
        {
            var dialog = new PaydayDialog([s]);
            if (MainWindow.Instance is { } w) dialog.Owner = w;
            if (dialog.ShowDialog() == true && dialog.Recorded > 0) Toaster.Show($"Added {s.Name}. Next payday {s.NextDate:MMM d}.");
            return;
        }
        Paydays.Receive(s, s.Amount, DateTime.Today);
        Toaster.Show($"Added {Money.Format(s.Amount)} from {s.Name}. Next payday {s.NextDate:MMM d}.");
    }
}
