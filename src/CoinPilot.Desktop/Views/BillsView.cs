using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        var leftThisMonth = open.Where(b => b.NextDue.Date <= Budgeting.MonthEnd(Budgeting.MonthStart(today))).ToList();
        var subscriptions = open.Where(b => string.Equals(b.Category, "Subscriptions", StringComparison.OrdinalIgnoreCase)).ToList();

        Add(PageKit.Header("", "Bills", "Never miss a due date."));
        Add(PageKit.ActionBar(DataStore.Prefs.RemindersOn
                ? $"Reminders {DataStore.Prefs.ReminderDays} days before due dates. Mark paid adds the payment to Transactions."
                : "Mark paid adds the payment to Transactions and moves the bill to its next due date.",
            PageKit.Primary("", "Add bill", (_, _) => BillDialog.Edit(null))));

        Add(PageKit.KpiRow(
            PageKit.Kpi("Due in the next 7 days", Money.Format(week.Sum(b => b.Amount)),
                overdue > 0 ? $"{overdue} overdue" : week.Count == 1 ? "1 bill" : $"{week.Count} bills", hero: true),
            PageKit.Kpi("Still to pay this month", Money.Format(leftThisMonth.Sum(b => b.Amount)),
                leftThisMonth.Count == 1 ? "1 bill" : $"{leftThisMonth.Count} bills"),
            PageKit.Kpi("Bills per month", Money.Format(open.Sum(BillSchedule.MonthlyCost)), "Average, all repeating bills"),
            PageKit.Kpi("Subscriptions", Money.Format(subscriptions.Sum(BillSchedule.MonthlyCost)) + "/mo",
                subscriptions.Count == 1 ? "1 subscription" : $"{subscriptions.Count} subscriptions")));

        BuildSuggestions(today);

        if (bills.Count == 0)
        {
            Add(PageKit.Card(Ui.EmptyState("\uE787", "No bills yet",
                "Add rent, utilities, phone, insurance and subscriptions with their due dates. WillowWallet shows what's coming up, reminds you before they're due and spots subscriptions you forgot.",
                "Add bill", (_, _) => BillDialog.Edit(null))));
            return;
        }

        var viewBar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        if (ShowCalendar)
        {
            var nav = new StackPanel { Orientation = Orientation.Horizontal };
            var prev = new Button { Content = "\uE76B", Style = Res("IconButton"), ToolTip = "Previous month" };
            prev.Click += (_, _) => { _calendarMonth = _calendarMonth.AddMonths(-1); Rebuild(); };
            var next = new Button { Content = "\uE76C", Style = Res("IconButton"), ToolTip = "Next month" };
            next.Click += (_, _) => { _calendarMonth = _calendarMonth.AddMonths(1); Rebuild(); };
            var label = Ui.Text(_calendarMonth.ToString("MMMM yyyy", Money.Us), size: 15, weight: FontWeights.SemiBold);
            label.Width = 150;
            label.TextAlignment = TextAlignment.Center;
            nav.Children.Add(prev);
            nav.Children.Add(label);
            nav.Children.Add(next);
            DockPanel.SetDock(nav, Dock.Right);
            viewBar.Children.Add(nav);
        }
        viewBar.Children.Add(PageKit.ChoiceChips(["List", "Calendar"], ShowCalendar ? "Calendar" : "List", pick => { ShowCalendar = pick == "Calendar"; Rebuild(); }));
        Add(viewBar);

        if (ShowCalendar)
        {
            Add(PageKit.Card(BuildCalendar(_calendarMonth, today), padding: new Thickness(12)));
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

    /// <summary>Calendar (true) or list (false); remembered while the app is open.</summary>
    public static bool ShowCalendar { get; set; }
    private static DateTime _calendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    // ----- Subscriptions you might have forgotten -----

    private void BuildSuggestions(DateTime today)
    {
        var guesses = SubscriptionFinder.Find(today);
        if (guesses.Count == 0) return;

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "These look like regular charges that aren't on your bills list yet. Add them so you can see them coming, or dismiss the ones that aren't.",
            Style = Res("Muted"), Margin = new Thickness(0, 0, 0, 8),
        });
        foreach (var g in guesses.Take(6))
        {
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(PageKit.Link("Add as bill", () => AddGuess(g)));
            buttons.Children.Add(PageKit.Link("Not a bill", () =>
            {
                DataStore.Prefs.DismissedSubscriptions.Add(g.Payee);
                DataStore.Prefs.Save();
            }));
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);
            var amount = Ui.Text($"{Money.Format(g.Amount)} · {g.Repeats.ToLowerInvariant()}", muted: true);
            amount.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(amount, Dock.Right);
            row.Children.Add(amount);
            var name = new StackPanel();
            name.Children.Add(Ui.Text(g.Payee, weight: FontWeights.SemiBold));
            name.Children.Add(Ui.Text($"{g.Charges} charges, last on {g.LastCharge:MMM d}", muted: true, size: 12));
            row.Children.Add(name);
            panel.Children.Add(row);
        }
        Add(PageKit.Card(panel, guesses.Count == 1 ? "1 possible subscription" : $"{guesses.Count} possible subscriptions"));
    }

    private static void AddGuess(SubscriptionGuess g)
    {
        DataStore.Bills.Add(new Bill
        {
            Name = g.Payee,
            Amount = g.Amount,
            NextDue = g.NextDue,
            DueDay = g.NextDue.Day,
            Repeats = g.Repeats,
            Category = Categories.Spending.Contains("Subscriptions") ? "Subscriptions" : g.Category,
            Account = g.Account,
        });
        Toaster.Show($"Added {g.Payee}, next due {g.NextDue:MMM d}.");
    }

    // ----- Calendar -----

    /// <summary>Every due date of every bill in a month (repeating bills projected forward).</summary>
    public static List<(DateTime Date, Bill Bill, bool Paid)> Occurrences(DateTime month)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var result = new List<(DateTime, Bill, bool)>();
        foreach (var b in DataStore.Bills.Items)
        {
            if (b.IsDone)
            {
                if (b.LastPaid is DateTime p && p >= first && p <= last) result.Add((b.NextDue, b, true));
                continue;
            }
            if (b.LastPaid is DateTime paid && paid >= first && paid <= last && b.Repeats != BillSchedule.Once)
            {
                // The payment already made this month, shown as paid on the date it was paid.
                result.Add((paid, b, true));
            }
            var d = b.NextDue.Date;
            int guard = 0;
            while (d <= last && guard++ < 60)
            {
                if (d >= first) result.Add((d, b, false));
                if (b.Repeats == BillSchedule.Once) break;
                d = BillSchedule.Next(d, b.Repeats, b.DueDay);
            }
        }
        return result;
    }

    private UIElement BuildCalendar(DateTime month, DateTime today)
    {
        var occurrences = Occurrences(month);
        var grid = new Grid();
        for (int c = 0; c < 7; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        string[] days = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int c = 0; c < 7; c++)
        {
            var t = Ui.Text(days[c], muted: true, size: 12, weight: FontWeights.SemiBold).At(c, new Thickness(12, 2, 8, 10));
            grid.Children.Add(t);
        }

        var first = new DateTime(month.Year, month.Month, 1);
        int offset = (int)first.DayOfWeek;
        int daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
        int weeks = (offset + daysInMonth + 6) / 7;
        for (int w = 0; w < weeks; w++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < weeks * 7; i++)
        {
            var date = first.AddDays(i - offset);
            bool inMonth = date.Month == month.Month;
            bool isToday = date == today.Date;
            bool weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            var cell = new Border
            {
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8, 7, 8, 8),
                MinHeight = 104,
                BorderThickness = new Thickness(1),
            };
            Grid.SetRow(cell, i / 7 + 1);
            Grid.SetColumn(cell, i % 7);
            cell.SetResourceReference(Border.BackgroundProperty, !inMonth ? "Brush.Bg" : weekend ? "Brush.SurfaceAlt" : "Brush.Surface");
            cell.SetResourceReference(Border.BorderBrushProperty, isToday ? "Brush.Accent" : "Brush.Border");
            if (isToday) cell.BorderThickness = new Thickness(1.5);

            var stack = new StackPanel();
            // Day number; today's sits in a filled circle
            var number = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-2, -1, 0, 2) };
            var numberText = Ui.Text(date.Day.ToString(), size: 12.5, weight: isToday ? FontWeights.Bold : FontWeights.SemiBold,
                brushKey: isToday ? "Brush.AccentText" : inMonth ? "Brush.Text" : "Brush.TextMuted");
            numberText.HorizontalAlignment = HorizontalAlignment.Center;
            numberText.VerticalAlignment = VerticalAlignment.Center;
            number.Child = numberText;
            if (isToday) number.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
            stack.Children.Add(number);
            if (!inMonth)
            {
                cell.Opacity = 0.55;
                cell.Child = stack;
                grid.Children.Add(cell);
                continue;
            }

            var due = occurrences.Where(o => o.Date.Date == date).OrderBy(o => o.Paid).ThenBy(o => o.Bill.Name).ToList();
            foreach (var (_, bill, paid) in due.Take(3))
            {
                bool overdue = !paid && date < today.Date;
                bool soon = !paid && !overdue && (date - today.Date).TotalDays <= 3;
                string dot = paid ? "Brush.Positive" : overdue ? "Brush.Danger" : soon ? "Brush.Warn" : "Brush.Accent";

                var chip = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 4, 7, 5), Margin = new Thickness(0, 4, 0, 0), Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(1) };
                chip.SetResourceReference(Border.BackgroundProperty, weekend ? "Brush.Surface" : "Brush.SurfaceAlt");
                chip.BorderBrush = Brushes.Transparent;
                chip.MouseEnter += (_, _) => chip.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
                chip.MouseLeave += (_, _) => chip.BorderBrush = Brushes.Transparent;

                var row = new DockPanel();
                var mark = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                mark.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, dot);
                DockPanel.SetDock(mark, Dock.Left);
                row.Children.Add(mark);
                var name = Ui.Text(bill.Name, size: 11.5, weight: FontWeights.SemiBold, brushKey: paid ? "Brush.TextMuted" : "Brush.Text");
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                if (paid) name.TextDecorations = TextDecorations.Strikethrough;
                row.Children.Add(name);
                var inner = new StackPanel();
                inner.Children.Add(row);
                var amount = Ui.Text(paid ? "Paid" : Money.Format(bill.Amount), muted: true, size: 11);
                amount.Margin = new Thickness(13, 0, 0, 0);
                inner.Children.Add(amount);
                chip.Child = inner;
                chip.ToolTip = paid ? $"{bill.Name}: paid" : overdue ? $"{bill.Name}: {Money.Format(bill.Amount)}, overdue since {date:MMM d}" : $"{bill.Name}: {Money.Format(bill.Amount)} due {date:MMM d}";
                var b = bill;
                chip.MouseLeftButtonUp += (_, _) => BillDialog.Edit(b);
                stack.Children.Add(chip);
            }
            if (due.Count > 3)
            {
                var more = Ui.Text($"+{due.Count - 3} more", size: 11, weight: FontWeights.SemiBold, brushKey: "Brush.Accent");
                more.Margin = new Thickness(4, 5, 0, 0);
                more.ToolTip = string.Join("\n", due.Skip(3).Select(o => $"{o.Bill.Name} · {Money.Format(o.Bill.Amount)}"));
                stack.Children.Add(more);
            }
            cell.Child = stack;
            grid.Children.Add(cell);
        }

        // Footer: totals and a key for the dots
        int paidCount = occurrences.Count(o => o.Paid);
        var total = occurrences.Where(o => !o.Paid).Sum(o => o.Bill.Amount);
        var foot = new DockPanel { Margin = new Thickness(6, 12, 6, 2) };
        var legend = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, key) in new[] { ("Due", "Brush.Accent"), ("Due soon", "Brush.Warn"), ("Overdue", "Brush.Danger"), ("Paid", "Brush.Positive") })
        {
            var d = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(14, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            d.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, key);
            legend.Children.Add(d);
            legend.Children.Add(Ui.Text(label, muted: true, size: 12));
        }
        DockPanel.SetDock(legend, Dock.Right);
        foot.Children.Add(legend);
        foot.Children.Add(Ui.Text($"{Money.Format(total)} still due in {month:MMMM} · {paidCount} paid", muted: true, size: 12.5));

        var wrap = new StackPanel();
        wrap.Children.Add(grid);
        wrap.Children.Add(foot);
        return wrap;
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
