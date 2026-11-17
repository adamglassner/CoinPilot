using CoinPilot.Services;

namespace CoinPilot.Data;

// Insights: spending alerts, the paycheck planner and this year compared with last.
//
// WillowWallet for Android, Mac and Linux does the same (mobile/lib/insights.dart); the self-test checks the same
// examples as its tests (mobile/test/insights_test.dart).

/// <summary>Something worth knowing now. Kind: over, budget, bill-price, unusual or dip.</summary>
public sealed record MoneyAlert(string Key, string Kind, string Title, string Body, string? BillId = null, decimal? NewAmount = null);

public sealed record PlanLine(string Kind, string Label, decimal Amount, DateTime Date, string Category = "");

public sealed record PaycheckPlan(IncomeSource Source, DateTime Payday, DateTime Next, decimal Amount, List<PlanLine> Lines, decimal Leftover)
{
    public decimal Total(string kind) => Lines.Where(l => l.Kind == kind).Sum(l => l.Amount);
}

public sealed record YearOverYear(DateTime From, DateTime To, decimal Spent, decimal SpentBefore, decimal Income, decimal IncomeBefore,
    List<(string Category, decimal Now, decimal Before)> Categories)
{
    public bool HasLastYear => SpentBefore > 0 || IncomeBefore > 0;
    /// <summary>+12 for 12% more than last year; null without last year's numbers.</summary>
    public static int? Change(decimal now, decimal before) => before <= 0 ? null : (int)Math.Round((now - before) * 100 / before, MidpointRounding.AwayFromZero);
}

public static class Insights
{
    private static int Floor(decimal v) => (int)Math.Floor(v);

    /// <summary>Alerts for today, most urgent first (none when alerts are off; dismissed ones left out).</summary>
    public static List<MoneyAlert> Alerts(DateTime now, bool includeDismissed = false)
    {
        var p = DataStore.Prefs;
        if (!p.AlertsOn) return [];
        var today = now.Date;
        var month = Budgeting.MonthStart(today);
        string mKey = month.ToString("yyyy-MM");
        int daysLeft = (Budgeting.MonthEnd(month) - today).Days + 1;
        var list = new List<MoneyAlert>();
        var txns = DataStore.Transactions.Items;

        // 1. Budgets: over, or past the warning level with days to go
        foreach (var s in Budgeting.Summarize(month).Budgets)
        {
            if (s.Available <= 0) continue;
            decimal pct = s.Spent * 100 / s.Available;
            string cat = s.Budget.Category;
            if (s.Spent > s.Available)
                list.Add(new MoneyAlert($"over|{cat}|{mKey}", "over", "Over budget: " + cat,
                    $"{Money.Format(s.Spent, cents: false)} of {Money.Format(s.Available, cents: false)}, {Money.Format(s.Spent - s.Available, cents: false)} over with {daysLeft} day{(daysLeft == 1 ? "" : "s")} left."));
            else if (pct >= Math.Clamp(p.AlertPercent, 50, 99) && daysLeft > 1)
                list.Add(new MoneyAlert($"budget|{cat}|{mKey}", "budget", $"{cat}: {Floor(pct)}% used",
                    $"{Money.Format(s.Available - s.Spent, cents: false)} left for {daysLeft} days."));
        }

        // 2. Bills whose price changed: the last two charges agree with each other but not with the bill
        foreach (var b in DataStore.Bills.Items)
        {
            if (b.IsDone || b.Amount <= 0) continue;
            string name = SubscriptionFinder.Normalize(b.Name);
            if (name.Length < 3) continue;
            var charges = txns.Where(t => t.IsSpending && !t.IsSplit && SubscriptionFinder.Normalize(t.Payee).Contains(name) && t.Date > today.AddDays(-100))
                .OrderByDescending(t => t.Date).ToList();
            if (charges.Count < 2) continue;
            decimal a = -charges[0].Amount, c = -charges[1].Amount;
            if (Math.Abs(a - c) > Math.Max(1m, Math.Floor(a) / 100)) continue;
            decimal diff = a - b.Amount;
            if (Math.Abs(diff) < Math.Max(1m, Math.Floor(b.Amount * 100 / 50) / 100)) continue;
            list.Add(new MoneyAlert($"bill-price|{b.Id}|{(int)(a * 100)}", "bill-price", diff > 0 ? b.Name + " went up" : b.Name + " went down",
                $"The last two charges were {Money.Format(a)}, but the bill says {Money.Format(b.Amount)}.", b.Id, a));
        }

        // 3. Unusual charges in the last week: much more than this payee usually costs
        foreach (var t in txns)
        {
            if (!t.IsSpending || (today - t.Date.Date).Days > 7 || t.Date.Date > today.AddDays(1)) continue;
            string key = SubscriptionFinder.Normalize(t.Payee);
            if (key.Length == 0) continue;
            var before = txns.Where(x => x.IsSpending && x.Id != t.Id && x.Date < t.Date && SubscriptionFinder.Normalize(x.Payee) == key).Select(x => -x.Amount).ToList();
            if (before.Count < 3) continue;
            decimal usual = Math.Round(before.Average(), 2, MidpointRounding.AwayFromZero);
            decimal amount = -t.Amount;
            if (amount < usual * 1.4m || amount - usual < 20m) continue;
            int more = (int)Math.Round((amount / usual - 1) * 100, MidpointRounding.AwayFromZero);
            list.Add(new MoneyAlert("unusual|" + t.Id, "unusual", $"{t.Payee}: {Money.Format(amount)}", $"About {more}% more than usual ({Money.Format(usual)})."));
        }

        // 4. The forecast dips below your cushion before the end of the month
        var f = Forecast.Run(today, "Month");
        var low = f.LowPoint;
        if (low.Expected < Math.Max(0, f.Target) && low.Date < f.End)
            list.Add(new MoneyAlert("dip|" + mKey, "dip", $"Your balance may dip to {Money.Format(low.Expected, cents: false)}",
                $"On {low.Date:MMM d}{(f.NextPayday is DateTime np && low.Date < np ? ", before payday" : "")}. See Forecast."));

        var order = new Dictionary<string, int> { ["over"] = 0, ["dip"] = 1, ["budget"] = 2, ["bill-price"] = 3, ["unusual"] = 4 };
        list = list.OrderBy(a => order[a.Kind]).ToList();
        if (includeDismissed) return list;
        var gone = p.DismissedAlerts.ToHashSet();
        return list.Where(a => !gone.Contains(a.Key)).ToList();
    }

    public static void Dismiss(MoneyAlert a)
    {
        var list = DataStore.Prefs.DismissedAlerts;
        if (list.Count > 200) list.RemoveRange(0, list.Count - 150);
        list.Add(a.Key);
        DataStore.Prefs.Save();
        DataStore.RaiseChanged();
    }

    /// <summary>"Update the bill" from a price-change alert.</summary>
    public static void ApplyBillPrice(MoneyAlert a)
    {
        var b = a.BillId == null ? null : DataStore.Bills.Find(a.BillId);
        if (b == null || a.NewAmount is not decimal amount) return;
        b.Amount = amount;
        DataStore.Bills.Update(b);
    }

    // ----- Paycheck planner -----

    /// <summary>How a paycheck could be split until the next one: bills due, budget money for those days, debt payments and savings, and what's left over.</summary>
    public static PaycheckPlan PlanPaycheck(IncomeSource s, DateTime payday, decimal? amount = null)
    {
        var from = payday.Date;
        var next = s.Repeats == IncomeSchedule.Once ? from.AddMonths(1) : IncomeSchedule.Next(from, s.Repeats, s.Day1, s.Day2);
        var until = next.AddDays(-1);
        decimal pay = amount ?? (s.Amount > 0 && !s.Varies ? s.Amount : Math.Round(TypicalCents(s), 2));
        var lines = new List<PlanLine>();

        foreach (var b in DataStore.Bills.Items)
        {
            if (b.IsDone) continue;
            var d = b.NextDue.Date;
            for (int i = 0; i < 60 && d <= until; i++)
            {
                if (d >= from || i == 0) lines.Add(new PlanLine("bill", b.Name, b.Amount, d < from ? from : d, Categories.Root(b.Category)));
                if (b.Repeats == BillSchedule.Once) break;
                d = BillSchedule.Next(d, b.Repeats, b.DueDay);
            }
        }

        int days = (until - from).Days + 1;
        var today = DateTime.Today;
        decimal budgetMoney = 0;
        for (var d = from; d <= until;)
        {
            var m = Budgeting.MonthStart(d);
            var mEnd = Budgeting.MonthEnd(m);
            var segEnd = mEnd < until ? mEnd : until;
            int segDays = (segEnd - d).Days + 1, left = (mEnd - d).Days + 1;
            var billsHere = lines.Where(l => l.Kind == "bill" && Budgeting.InMonth(l.Date, m))
                .GroupBy(l => l.Category, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount), StringComparer.OrdinalIgnoreCase);
            bool current = m <= today;
            var summary = current ? Budgeting.Summarize(m) : null;
            foreach (var b in DataStore.Budgets.Items)
            {
                var st = summary?.Budgets.FirstOrDefault(x => x.Budget.Id == b.Id);
                decimal baseLeft = current ? (st?.Available ?? 0) - (st?.Spent ?? 0) : Budgeting.LimitFor(b, m);
                decimal remaining = Math.Max(0, baseLeft - billsHere.GetValueOrDefault(b.Category));
                budgetMoney += Math.Round(remaining * segDays / left, 2, MidpointRounding.AwayFromZero);
            }
            d = segEnd.AddDays(1);
        }
        if (budgetMoney > 0) lines.Add(new PlanLine("budget", $"Budgets for {days} days", budgetMoney, from));

        var linked = DataStore.Bills.Items.Where(b => b.PaysDebt).Select(b => b.DebtId).ToHashSet();
        decimal share = days / 30.44m;
        foreach (var d in DataStore.Debts.Items.Where(d => !d.IsPaidOff && d.MinimumPayment > 0 && !linked.Contains(d.Id)))
            lines.Add(new PlanLine("debt", d.Name, Math.Round(d.MinimumPayment * share, 2, MidpointRounding.AwayFromZero), from));
        foreach (var g in DataStore.Goals.Items)
            if (GoalMath.MonthlyNeeded(g, from) is decimal need && need > 0)
                lines.Add(new PlanLine("savings", g.Name, Math.Round(need * share, 2, MidpointRounding.AwayFromZero), from));

        return new PaycheckPlan(s, from, next, pay, lines, pay - lines.Sum(l => l.Amount));
    }

    private static decimal TypicalCents(IncomeSource s)
    {
        var last = DataStore.Transactions.Items.Where(t => t.IsIncome && string.Equals(t.Payee, s.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.Date).Take(4).Select(t => t.Amount).ToList();
        return last.Count == 0 ? s.Amount : last.Average();
    }

    // ----- This year vs last -----

    /// <summary>Spending by category for <paramref name="from"/>–<paramref name="to"/> and the same days a year earlier, biggest changes first.</summary>
    public static YearOverYear Compare(DateTime from, DateTime to)
    {
        static DateTime Back(DateTime d) => d.AddYears(-1);
        var txns = DataStore.Transactions.Items;
        var now = Reports.ByCategory(from, to, txns, rollUp: true).ToDictionary(x => x.Category, x => x.Spent, StringComparer.OrdinalIgnoreCase);
        var then = Reports.ByCategory(Back(from), Back(to), txns, rollUp: true).ToDictionary(x => x.Category, x => x.Spent, StringComparer.OrdinalIgnoreCase);
        decimal Income(DateTime a, DateTime b) => txns.Where(t => t.IsIncome && t.Date.Date >= a && t.Date.Date <= b).Sum(t => t.Amount);
        var cats = now.Keys.Union(then.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(c => (c, now.GetValueOrDefault(c), then.GetValueOrDefault(c)))
            .OrderByDescending(x => Math.Abs(x.Item2 - x.Item3)).ToList();
        return new YearOverYear(from, to, now.Values.Sum(), then.Values.Sum(), Income(from, to), Income(Back(from), Back(to)), cats);
    }
}
