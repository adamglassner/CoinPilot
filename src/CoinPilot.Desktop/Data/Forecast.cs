namespace CoinPilot.Data;

// Forecast: where your money is headed.
//
// Starting from what's in your spending accounts today, it adds paychecks on their paydays and takes away bills on
// their due dates, what's left in each budget (spread over the days left in the month), other everyday spending, debt
// payments and money set aside for goals and sinking funds. The result is a balance for every day up to the end of the
// month (or 30, 60 or 90 days), with a likely range, the lowest point, and what moves the number most.
//
// WillowWallet for Android, Mac and Linux does exactly the same math (mobile/lib/forecast.dart); the self-test checks
// the same example as its tests (mobile/test/forecast_test.dart).

/// <summary>Something that changes the balance on a day. Kind: income, bill, budget, other, debt, savings or whatif.</summary>
public sealed record ForecastEvent(DateTime Date, string Label, string Group, decimal Amount, string Kind, decimal? Low = null, decimal? High = null)
{
    public decimal Worst => Low ?? Amount;
    public decimal Best => High ?? Amount;
}

/// <summary>A "what if" to try without saving anything (+ money in, − money out).</summary>
public sealed record WhatIf(string Label, decimal Amount, DateTime Date);

public sealed record ForecastDay(DateTime Date, decimal Expected, decimal Low, decimal High);

public sealed record ForecastDriver(string Label, decimal Amount, string Kind);

public enum ForecastVerdict { Over, Under, OnTarget }

public sealed class ForecastResult
{
    public required DateTime Today { get; init; }
    public required DateTime End { get; init; }
    public required decimal Start { get; init; }
    public required decimal Target { get; init; }
    public required List<ForecastDay> Days { get; init; }
    public required List<ForecastEvent> Events { get; init; }
    public required List<string> Accounts { get; init; }
    public DateTime? NextPayday { get; init; }

    /// <summary>Allowed difference for "right on target": $25.</summary>
    public const decimal Tolerance = 25m;

    public decimal EndExpected => Days[^1].Expected;
    public decimal EndLow => Days[^1].Low;
    public decimal EndHigh => Days[^1].High;
    public decimal Difference => EndExpected - Target;

    public ForecastVerdict Verdict => Math.Abs(Difference) <= Tolerance ? ForecastVerdict.OnTarget
        : Difference > 0 ? ForecastVerdict.Over : ForecastVerdict.Under;

    public ForecastDay LowPoint => Days.Aggregate((a, b) => b.Expected < a.Expected ? b : a);

    /// <summary>Spending beyond your budgets that still leaves the target (or your cushion) in place until the next payday.</summary>
    public decimal SafeToSpend
    {
        get
        {
            var until = NextPayday ?? End;
            decimal low = Days[0].Expected;
            foreach (var d in Days)
            {
                if (d.Date >= until) break;
                low = Math.Min(low, d.Expected);
            }
            return Math.Max(0, low - Math.Max(0, Target));
        }
    }

    /// <summary>"High", "Medium" or "Low": how far apart the best and worst cases are.</summary>
    public string Confidence
    {
        get
        {
            decimal spread = EndHigh - EndLow;
            decimal scale = Math.Max(0.01m, Events.Where(e => e.Amount > 0).Sum(e => e.Amount));
            double r = (double)(spread / scale);
            return r < 0.04 ? "High" : r < 0.15 ? "Medium" : "Low";
        }
    }

    /// <summary>What moves the balance most, biggest first, added up by what they are.</summary>
    public List<ForecastDriver> Drivers(int count = 6) =>
        Events.GroupBy(e => e.Group)
            .Select(g => new ForecastDriver(g.Key, g.Sum(e => e.Amount), g.Last().Kind))
            .Where(d => d.Amount != 0)
            .OrderByDescending(d => Math.Abs(d.Amount))
            .Take(count).ToList();

    public List<ForecastEvent> On(DateTime day) => Events.Where(e => e.Date.Date == day.Date).ToList();
    public ForecastDay? DayOf(DateTime d) => Days.FirstOrDefault(x => x.Date == d.Date);
}

public static class Forecast
{
    public static readonly string[] Horizons = ["Month", "30", "60", "90"];
    public static string HorizonLabel(string h) => h == "Month" ? "This month" : h + " days";

    /// <summary>Accounts the forecast counts: checking, cash and payment apps (and savings when chosen).</summary>
    public static List<Account> Accounts()
    {
        var types = new List<string> { AccountTypes.Checking, AccountTypes.Cash, AccountTypes.PaymentApp };
        if (DataStore.Prefs.ForecastIncludeSavings) types.Add(AccountTypes.Savings);
        return DataStore.Accounts.Items.Where(a => !a.IsClosed && types.Contains(a.Type)).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The day the forecast runs to: the end of the budget month, or 30, 60 or 90 days from today.</summary>
    public static DateTime EndFor(DateTime today, string? horizon = null) => (horizon ?? DataStore.Prefs.ForecastHorizon) switch
    {
        "30" => today.Date.AddDays(30),
        "60" => today.Date.AddDays(60),
        "90" => today.Date.AddDays(90),
        _ => Budgeting.MonthEnd(Budgeting.MonthStart(today)),
    };

    private static decimal Cents(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static decimal SpentFor(Budget b, DateTime month, IReadOnlyCollection<Transaction> txns, IEnumerable<Budget> budgets) =>
        Budgeting.Summarize(month, txns, budgets).Budgets.FirstOrDefault(s => s.Budget.Id == b.Id)?.Spent ?? 0;

    /// <summary>How much more than its budget a category usually spends: the average and the highest of the last three months' spent ÷ budget (1 without enough history).</summary>
    public static (decimal Usual, decimal Worst) Habit(Budget b, DateTime today)
    {
        var month = Budgeting.MonthStart(today);
        var txns = DataStore.Transactions.Items;
        var budgets = DataStore.Budgets.Items;
        var since = Budgeting.MonthStart(b.StartMonth ?? (b.CreatedAt == default ? month : b.CreatedAt));
        var ratios = new List<decimal>();
        for (int i = 1; i <= 3; i++)
        {
            var m = month.AddMonths(-i);
            if (m < since) continue;
            decimal limit = Budgeting.LimitFor(b, m);
            if (limit <= 0) continue;
            ratios.Add(SpentFor(b, m, txns, budgets) / limit);
        }
        if (ratios.Count < 2) return (1m, 1m);
        return (Math.Clamp(ratios.Average(), 1m, 1.5m), Math.Clamp(ratios.Max(), 1m, 2m));
    }

    private static bool HasBudget(string category) =>
        DataStore.Budgets.Items.Any(b => string.Equals(b.Category, category, StringComparison.OrdinalIgnoreCase)
            || string.Equals(b.Category, Categories.Root(category), StringComparison.OrdinalIgnoreCase));

    private static decimal OtherSpent(DateTime month, HashSet<string> billCats)
    {
        var summary = Budgeting.Summarize(month);
        decimal total = 0;
        foreach (var (c, spent) in summary.SpentByCategory)
        {
            if (billCats.Contains(c) || billCats.Contains(Categories.Root(c))) continue;
            if (HasBudget(c)) continue;
            total += spent;
        }
        return total;
    }

    /// <summary>Average monthly spending in categories with no budget and no bills (the last three months).</summary>
    public static decimal OtherSpendingPerMonth(DateTime today)
    {
        var month = Budgeting.MonthStart(today);
        var billCats = DataStore.Bills.Items.Select(b => b.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        decimal total = 0;
        for (int i = 1; i <= 3; i++) total += OtherSpent(month.AddMonths(-i), billCats);
        // Whole cents, rounded down like the phone app's integer division
        return Math.Floor(total * 100 / 3) / 100;
    }

    public static ForecastResult Run(DateTime now, string? horizon = null, DateTime? until = null, IEnumerable<WhatIf>? whatIfs = null,
        ISet<string>? skipBills = null, IReadOnlyDictionary<string, decimal>? budgetChanges = null)
    {
        var today = now.Date;
        var end = until?.Date ?? EndFor(today, horizon);
        var prefs = DataStore.Prefs;
        var accounts = Accounts();
        var txns = DataStore.Transactions.Items;
        decimal start = accounts.Sum(a => AccountMath.BalanceAt(a, today, txns));
        var events = new List<ForecastEvent>();
        DateTime Clamp(DateTime d) => d.Date < today ? today : d.Date;
        DateTime? nextPayday = null;

        // 1. Paychecks
        foreach (var s in DataStore.Income.Items)
        {
            if (s.IsDone) continue;
            bool fixedAmount = s.Amount > 0 && !s.Varies;
            decimal each = fixedAmount ? s.Amount : Cents(TypicalExact(s));
            decimal low = each, high = each;
            if (!fixedAmount)
            {
                var last = txns.Where(t => t.IsIncome && string.Equals(t.Payee, s.Name, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(t => t.Date).Take(4).Select(t => t.Amount).ToList();
                if (last.Count > 0) { low = last.Min(); high = last.Max(); }
            }
            if (each <= 0) continue;
            foreach (var d in IncomeSchedule.Paydays(s, new DateTime(2000, 1, 1), end))
            {
                var day = Clamp(d);
                if (day > today && (nextPayday == null || day < nextPayday)) nextPayday = day;
                events.Add(new ForecastEvent(day, s.Name, "Paycheck: " + s.Name, each, "income", low, high));
            }
        }

        // 2. Bills (and how much of each month's bills land in each category, so budgets don't count them twice)
        var billsByMonthCat = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in DataStore.Bills.Items)
        {
            if (b.IsDone || (skipBills?.Contains(b.Id) ?? false)) continue;
            // A bill charged to a credit card leaves your cash when the card is paid, not on its due date
            var card = CreditCards.For(b);
            var d = b.NextDue.Date;
            for (int i = 0; i < 200 && d <= end; i++)
            {
                var day = Clamp(d);
                if (card != null)
                {
                    var pay = Clamp(CreditCards.PaymentDate(day, card.PaymentDueDay, card.StatementDay));
                    if (pay <= end) events.Add(new ForecastEvent(pay, $"{b.Name} on {card.Name}", "Card payment: " + card.Name, -b.Amount, "bill"));
                }
                else events.Add(new ForecastEvent(day, b.Name, "Bill: " + b.Name, -b.Amount, "bill"));
                string key = Budgeting.Key(day) + "|" + Categories.Root(b.Category);
                billsByMonthCat[key] = billsByMonthCat.GetValueOrDefault(key) + b.Amount;
                if (b.Repeats == BillSchedule.Once) break;
                d = BillSchedule.Next(d, b.Repeats, b.DueDay);
            }
        }

        // 3. Budgets and other everyday spending, spread over the days left in each budget month
        decimal other = OtherSpendingPerMonth(today);
        var billCats = DataStore.Bills.Items.Select(b => b.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var m = Budgeting.MonthStart(today); m <= end; m = m.AddMonths(1))
        {
            var mEnd = Budgeting.MonthEnd(m);
            var from = m < today ? today : m;
            var to = mEnd > end ? end : mEnd;
            int daysLeft = (mEnd - from).Days + 1;
            int daysIn = (to - from).Days + 1;
            if (daysIn <= 0) continue;
            bool current = m <= today;
            var summary = current ? Budgeting.Summarize(m) : null;
            foreach (var b in DataStore.Budgets.Items)
            {
                decimal limit = budgetChanges != null && budgetChanges.TryGetValue(b.Category, out var changed) ? changed : Budgeting.LimitFor(b, m);
                var status = summary?.Budgets.FirstOrDefault(x => x.Budget.Id == b.Id);
                decimal available = current ? limit + (status?.Carry ?? 0) : limit;
                decimal spent = current ? status?.Spent ?? 0 : 0;
                decimal billsHere = billsByMonthCat.GetValueOrDefault(Budgeting.Key(m) + "|" + b.Category);
                var (usual, worst) = Habit(b, today);
                decimal Left(decimal habit) => Math.Max(0, Math.Max(available, Cents(limit * habit)) - spent - billsHere);
                decimal expected = Left(usual), best = Math.Max(0, available - spent - billsHere), worstLeft = Left(worst);
                if (expected == 0 && worstLeft == 0) continue;
                decimal share = (decimal)daysIn / daysLeft;
                events.Add(new ForecastEvent(to, b.Category + " budget", "Budget: " + b.Category, -Cents(expected * share), "budget",
                    -Cents(worstLeft * share), -Cents(best * share)));
            }
            if (other > 0)
            {
                decimal spentSoFar = current ? OtherSpent(m, billCats) : 0;
                decimal left = Math.Max(0, other - spentSoFar);
                if (left > 0)
                    events.Add(new ForecastEvent(to, "Other spending", "Other everyday spending", -Cents(left * daysIn / daysLeft), "other"));
            }
        }

        // 4. Debt payments that aren't bills: the minimum (and the plan's extra) at the end of each month
        var linked = DataStore.Bills.Items.Where(b => b.PaysDebt).Select(b => b.DebtId).ToHashSet();
        var unlinked = DataStore.Debts.Items.Where(d => !d.IsPaidOff && d.MinimumPayment > 0 && !linked.Contains(d.Id)).ToList();
        for (var m = Budgeting.MonthStart(today); m <= end; m = m.AddMonths(1))
        {
            var mEnd = Budgeting.MonthEnd(m);
            if (mEnd > end) continue;
            foreach (var d in unlinked)
            {
                if (d.Payments.Any(x => Budgeting.InMonth(x.Date, m))) continue;
                events.Add(new ForecastEvent(mEnd, d.Name, "Debt payments", -d.MinimumPayment, "debt"));
            }
            if (unlinked.Count > 0 && DataStore.DebtPlan.ExtraPayment > 0)
                events.Add(new ForecastEvent(mEnd, "Extra debt payment", "Debt payments", -DataStore.DebtPlan.ExtraPayment, "debt"));
        }

        // 5. Money set aside for goals and sinking funds (unless it stays in a counted account)
        if (prefs.ForecastCountSavings)
        {
            var counted = accounts.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var g in DataStore.Goals.Items)
            {
                if (g.IsLinked && counted.Contains(g.LinkedAccount)) continue;
                for (var m = Budgeting.MonthStart(today); m <= end; m = m.AddMonths(1))
                {
                    var mEnd = Budgeting.MonthEnd(m);
                    if (mEnd > end) continue;
                    if (g.History.Any(x => x.Amount > 0 && Budgeting.InMonth(x.Date, m))) continue;
                    var need = GoalMath.MonthlyNeeded(g, m < today ? today : m);
                    if (need is not decimal n || n <= 0) continue;
                    events.Add(new ForecastEvent(mEnd, g.Name, (g.IsFund ? "Sinking fund: " : "Goal: ") + g.Name, -n, "savings"));
                }
            }
        }

        // 6. What ifs
        foreach (var w in whatIfs ?? [])
        {
            var d = Clamp(w.Date);
            if (d <= end) events.Add(new ForecastEvent(d, w.Label, "What if: " + w.Label, w.Amount, "whatif"));
        }

        events = events.OrderBy(e => e.Date).ToList();

        // Budget and everyday spending is spread evenly; everything else lands on its day
        var days = new List<ForecastDay>();
        decimal exp = start, lo = start, hi = start;
        var spread = events.Where(e => e.Kind is "budget" or "other").ToList();
        for (var d = today; d <= end; d = d.AddDays(1))
        {
            foreach (var e in events)
            {
                if (e.Kind is "budget" or "other" || e.Date != d) continue;
                exp += e.Amount;
                lo += e.Worst;
                hi += e.Best;
            }
            foreach (var e in spread)
            {
                var m = Budgeting.MonthStart(e.Date);
                var from = m < today ? today : m;
                if (d < from || d > e.Date) continue;
                int n = (e.Date - from).Days + 1;
                int i = (d - from).Days;
                // Whole cents per day, so the days add up exactly
                decimal Part(decimal total) => (Math.Round(total * 100 * (i + 1) / n, MidpointRounding.AwayFromZero) - Math.Round(total * 100 * i / n, MidpointRounding.AwayFromZero)) / 100;
                exp += Part(e.Amount);
                lo += Part(e.Worst);
                hi += Part(e.Best);
            }
            days.Add(new ForecastDay(d, exp, lo, hi));
        }

        return new ForecastResult
        {
            Today = today,
            End = end,
            Start = start,
            Days = days,
            Events = events,
            Target = prefs.ForecastTarget,
            Accounts = accounts.Select(a => a.Name).ToList(),
            NextPayday = nextPayday,
        };
    }

    /// <summary>The average of the last few paychecks, unrounded (the phone app rounds it to the cent once).</summary>
    private static decimal TypicalExact(IncomeSource s)
    {
        var last = DataStore.Transactions.Items
            .Where(t => t.IsIncome && string.Equals(t.Payee, s.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.Date).Take(4).Select(t => t.Amount).ToList();
        return last.Count == 0 ? s.Amount : last.Average();
    }

    /// <summary>"You'll end November about $340 over", "$120 short" or "right on target".</summary>
    public static string VerdictText(ForecastResult f)
    {
        string when = DataStore.Prefs.ForecastHorizon == "Month" ? "end " + f.End.ToString("MMMM") : $"be on {f.End:MMM d}";
        string aim = f.Target == 0 ? "" : $" of your {Services.Money.Format(f.Target, cents: false)} target";
        return f.Verdict switch
        {
            ForecastVerdict.OnTarget => $"You'll {when} right on target",
            ForecastVerdict.Over => $"You'll {when} about {Services.Money.Format(f.Difference, cents: false)} over{aim}",
            _ => $"You'll {when} about {Services.Money.Format(-f.Difference, cents: false)} short{aim}",
        };
    }
}
