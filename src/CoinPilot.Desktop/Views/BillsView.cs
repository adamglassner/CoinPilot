using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>The Bills page: what's due and when, with one-click "Mark paid".</summary>
public sealed class BillsView : DataPage
{
    protected override void Build()
    {
        var today = DateTime.Today;
        var bills = DataStore.Bills.Items;
        var open = bills.Where(b => !b.IsDone).ToList();
        var week = open.Where(b => (b.NextDue.Date - today).Days <= 7).ToList();
        int overdue = open.Count(b => b.NextDue.Date < today);
        var leftThisMonth = open.Where(b => b.NextDue.Date <= new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month))).ToList();
        var subscriptions = open.Where(b => string.Equals(b.Category, "Subscriptions", StringComparison.OrdinalIgnoreCase)).ToList();

        Add(PageKit.Header("", "Bills", "Never miss a due date."));
        Add(PageKit.ActionBar("Mark paid adds the payment to Transactions and moves the bill to its next due date.",
            PageKit.Primary("", "Add bill", (_, _) => BillDialog.Edit(null))));

        Add(PageKit.KpiRow(
            PageKit.Kpi("Due in the next 7 days", Money.Format(week.Sum(b => b.Amount)),
                overdue > 0 ? $"{overdue} overdue" : week.Count == 1 ? "1 bill" : $"{week.Count} bills", hero: true),
            PageKit.Kpi("Still to pay this month", Money.Format(leftThisMonth.Sum(b => b.Amount)),
                leftThisMonth.Count == 1 ? "1 bill" : $"{leftThisMonth.Count} bills"),
            PageKit.Kpi("Bills per month", Money.Format(open.Sum(BillSchedule.MonthlyCost)), "Average, all repeating bills"),
            PageKit.Kpi("Subscriptions", Money.Format(subscriptions.Sum(BillSchedule.MonthlyCost)) + "/mo",
                subscriptions.Count == 1 ? "1 subscription" : $"{subscriptions.Count} subscriptions")));

        if (bills.Count == 0)
        {
            Add(PageKit.Card(Ui.EmptyState("", "No bills yet",
                "Add rent, utilities, phone, insurance and subscriptions with their due dates. CoinPilot shows what's coming up and what's overdue.",
                "Add bill", (_, _) => BillDialog.Edit(null))));
            return;
        }

        var list = new StackPanel();
        bool first = true;
        foreach (var b in open.OrderBy(b => b.NextDue).ThenBy(b => b.Name))
        {
            list.Children.Add(BuildRow(b, today, first));
            first = false;
        }
        var done = bills.Where(b => b.IsDone).OrderByDescending(b => b.LastPaid).ToList();
        if (done.Count > 0)
        {
            list.Children.Add(PageKit.Section("Paid one-time bills"));
            foreach (var b in done) list.Children.Add(BuildRow(b, today, false));
        }
        Add(PageKit.Card(list, padding: new Thickness(14, 8, 14, 8)));
    }

    private Button BuildRow(Bill b, DateTime today, bool firstRow)
    {
        var grid = new Grid { Margin = new Thickness(10, 12, 10, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

        // Date tile
        var tile = new Border { Width = 50, CornerRadius = new CornerRadius(10), Padding = new Thickness(0, 5, 0, 6), Margin = new Thickness(0, 0, 14, 0) };
        tile.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        var tileText = new StackPanel();
        var mon = Ui.Text(b.NextDue.ToString("MMM", Money.Us).ToUpperInvariant(), muted: true, size: 10.5, weight: FontWeights.SemiBold);
        mon.HorizontalAlignment = HorizontalAlignment.Center;
        var day = Ui.Text(b.NextDue.Day.ToString(), size: 18, weight: FontWeights.Bold);
        day.HorizontalAlignment = HorizontalAlignment.Center;
        tileText.Children.Add(mon);
        tileText.Children.Add(day);
        tile.Child = tileText;
        grid.Children.Add(tile);

        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center }.At(1);
        middle.Children.Add(Ui.Text(b.Name, weight: FontWeights.SemiBold, size: 14.5));
        var parts = new List<string> { b.Repeats, b.Category };
        if (b.Account.Length > 0) parts.Add(b.Account);
        if (b.AutoPay) parts.Add("Auto-pay");
        middle.Children.Add(Ui.Text(string.Join(" · ", parts), muted: true, size: 12.5));
        grid.Children.Add(middle);

        var status = BillSchedule.Status(b, today);
        string brush = status switch
        {
            BillStatus.Overdue => "Brush.Danger",
            BillStatus.DueToday or BillStatus.DueSoon => "Brush.Warn",
            BillStatus.Paid => "Brush.Positive",
            _ => "Brush.TextMuted",
        };
        grid.Children.Add(PageKit.Pill(BillSchedule.StatusText(b, today), brush).At(2));

        var amount = Ui.Text(Money.Format(b.Amount), weight: FontWeights.SemiBold, size: 14.5).At(3);
        amount.HorizontalAlignment = HorizontalAlignment.Right;
        grid.Children.Add(amount);

        if (!b.IsDone)
        {
            var pay = PageKit.Link("Mark paid", () => MarkPaid(b));
            pay.HorizontalAlignment = HorizontalAlignment.Right;
            grid.Children.Add(pay.At(4));
        }

        return PageKit.Row(grid, $"{b.Name}, {Money.Format(b.Amount)}, {BillSchedule.StatusText(b, today)}", () => BillDialog.Edit(b), firstRow);
    }

    private static void MarkPaid(Bill b)
    {
        var due = b.NextDue;
        BillSchedule.MarkPaid(b, DateTime.Today);
        Toaster.Show(b.Repeats == BillSchedule.Once
            ? $"Paid {b.Name} ({Money.Format(b.Amount)}). Added to Transactions."
            : $"Paid {b.Name} for {due:MMM d}. Added to Transactions; next due {b.NextDue:MMM d}.");
    }
}
