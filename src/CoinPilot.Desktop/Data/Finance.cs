namespace CoinPilot.Data;

// ======================================================================
// Accounts
// ======================================================================

public static class AccountMath
{
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>How a transaction changes this account: its amount, or for a transfer into it, the amount coming in.</summary>
    public static decimal Effect(Transaction t, string account)
    {
        decimal e = 0;
        if (Same(t.Account, account)) e += t.Amount;
        if (t.IsTransfer && Same(t.TransferAccount, account)) e += -t.Amount;
        return e;
    }

    /// <summary>Starting balance plus this account's transactions dated after the balance date.</summary>
    public static decimal Balance(Account a, IEnumerable<Transaction> transactions) =>
        BalanceAt(a, DateTime.MaxValue.Date, transactions);

    public static decimal Balance(Account a) => Balance(a, DataStore.Transactions.Items);

    /// <summary>The account's balance at the end of <paramref name="date"/> (works before the balance date too).</summary>
    public static decimal BalanceAt(Account a, DateTime date, IEnumerable<Transaction> transactions)
    {
        var start = a.BalanceDate.Date;
        date = date.Date;
        if (date >= start)
            return a.StartingBalance + transactions.Where(t => t.Date.Date > start && t.Date.Date <= date).Sum(t => Effect(t, a.Name));
        return a.StartingBalance - transactions.Where(t => t.Date.Date > date && t.Date.Date <= start).Sum(t => Effect(t, a.Name));
    }

    public static DateTime? LastActivity(Account a) =>
        DataStore.Transactions.Items
            .Where(t => Same(t.Account, a.Name) || (t.IsTransfer && Same(t.TransferAccount, a.Name)))
            .Select(t => (DateTime?)t.Date).Max();

    /// <summary>What a debt was at the end of a day: today's balance plus payments made after it (interest not included).</summary>
    public static decimal DebtAt(Debt d, DateTime date) =>
        Math.Max(0, d.Balance + d.Payments.Where(p => p.Date.Date > date.Date).Sum(p => p.Amount));

    /// <summary>Open accounts (assets minus card balances) minus everything on the Debts page.</summary>
    public static decimal NetWorth() =>
        DataStore.Accounts.Items.Where(a => !a.IsClosed).Sum(Balance)
        - DataStore.Debts.Items.Sum(d => Math.Max(0, d.Balance));

    /// <summary>Net worth at the end of each of the last <paramref name="months"/> months (the last point is today).</summary>
    public static List<(DateTime Date, decimal Value)> NetWorthHistory(int months)
    {
        var today = DateTime.Today;
        var tx = DataStore.Transactions.Items;
        var result = new List<(DateTime, decimal)>();
        for (int i = months - 1; i >= 0; i--)
        {
            var date = i == 0 ? today : new DateTime(today.Year, today.Month, 1).AddMonths(-i + 1).AddDays(-1);
            decimal value = DataStore.Accounts.Items.Sum(a => BalanceAt(a, date, tx))
                            - DataStore.Debts.Items.Sum(d => DebtAt(d, date));
            result.Add((date, value));
        }
        return result;
    }

    /// <summary>Renames an account everywhere it's used (transactions, transfers, bills and goals).</summary>
    public static void Rename(string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal) || oldName.Length == 0) return;
        using var _ = DataStore.Batch();
        var tx = DataStore.Transactions.Items.Where(t => Same(t.Account, oldName) || Same(t.TransferAccount, oldName)).ToList();
        foreach (var t in tx)
        {
            if (Same(t.Account, oldName)) t.Account = newName;
            if (Same(t.TransferAccount, oldName)) t.TransferAccount = newName;
        }
        if (tx.Count > 0) DataStore.Transactions.UpdateMany(tx);

        var bills = DataStore.Bills.Items.Where(b => Same(b.Account, oldName)).ToList();
        foreach (var b in bills) b.Account = newName;
        if (bills.Count > 0) DataStore.Bills.UpdateMany(bills);

        var goals = DataStore.Goals.Items.Where(g => Same(g.LinkedAccount, oldName)).ToList();
        foreach (var g in goals) g.LinkedAccount = newName;
        if (goals.Count > 0) DataStore.Goals.UpdateMany(goals);
    }
}

// ======================================================================
// Debts: payoff plan
// ======================================================================

public sealed class DebtPlanResult
{
    /// <summary>Month number (1 = next month) each debt is paid off in, by debt Id. Missing = never at this pace.</summary>
    public Dictionary<string, int> PayoffMonth { get; } = [];
    /// <summary>Order the plan attacks debts in (1 = first), by debt Id.</summary>
    public Dictionary<string, int> Order { get; } = [];
    public decimal TotalInterest { get; set; }
    public int Months { get; set; }
    /// <summary>True when the payments don't cover the interest on at least one debt.</summary>
    public bool NeverPaysOff { get; set; }
    public decimal MonthlyPayment { get; set; }

    public DateTime? DebtFreeDate(DateTime today) =>
        NeverPaysOff || Months == 0 ? null : new DateTime(today.Year, today.Month, 1).AddMonths(Months);
}

public static class DebtPlanner
{
    public const string Avalanche = "Avalanche";
    public const string Snowball = "Snowball";
    /// <summary>50 years. A plan that takes longer than this is treated as never paying off.</summary>
    private const int MaxMonths = 600;

    /// <summary>
    /// Simulates paying the debts month by month: interest is added, every debt gets its minimum payment, and the extra
    /// payment (plus the minimums of debts already paid off) goes to the first debt in the plan's order.
    /// Avalanche = highest interest rate first (least interest). Snowball = smallest balance first (quick wins).
    /// </summary>
    public static DebtPlanResult Plan(IEnumerable<Debt> debts, string strategy, decimal extra)
    {
        var active = debts.Where(d => d.Balance > 0).ToList();
        var ordered = strategy == Snowball
            ? active.OrderBy(d => d.Balance).ThenByDescending(d => d.InterestRate).ToList()
            : active.OrderByDescending(d => d.InterestRate).ThenBy(d => d.Balance).ToList();

        var result = new DebtPlanResult();
        for (int i = 0; i < ordered.Count; i++) result.Order[ordered[i].Id] = i + 1;
        if (ordered.Count == 0) return result;

        var balance = ordered.ToDictionary(d => d.Id, d => d.Balance);
        decimal monthly = ordered.Sum(d => d.MinimumPayment) + Math.Max(0, extra);
        result.MonthlyPayment = monthly;

        int month = 0;
        while (balance.Values.Any(b => b > 0) && month < MaxMonths)
        {
            month++;
            var open = ordered.Where(d => balance[d.Id] > 0).ToList();

            foreach (var d in open)
            {
                decimal interest = Math.Round(balance[d.Id] * d.InterestRate / 1200m, 2);
                balance[d.Id] += interest;
                result.TotalInterest += interest;
            }

            decimal available = monthly;
            foreach (var d in open)
            {
                decimal pay = Math.Min(Math.Min(d.MinimumPayment, balance[d.Id]), available);
                balance[d.Id] -= pay;
                available -= pay;
            }
            foreach (var d in open)
            {
                if (available <= 0) break;
                decimal pay = Math.Min(available, balance[d.Id]);
                balance[d.Id] -= pay;
                available -= pay;
            }

            foreach (var d in open.Where(d => balance[d.Id] <= 0.005m))
            {
                balance[d.Id] = 0;
                result.PayoffMonth[d.Id] = month;
            }

            // A debt that keeps growing will never be paid off at this pace; stop before the numbers explode.
            if (open.Any(d => balance[d.Id] > d.Balance * 10)) break;

        }

        result.Months = month;
        result.NeverPaysOff = balance.Values.Any(b => b > 0);
        return result;
    }

    public static DebtPlanResult CurrentPlan() =>
        Plan(DataStore.Debts.Items, DataStore.DebtPlan.Strategy, DataStore.DebtPlan.ExtraPayment);
}

// ======================================================================
// Bills
// ======================================================================

public enum BillStatus { Overdue, DueToday, DueSoon, Upcoming, Paid }

public static class BillSchedule
{
    public const string Weekly = "Weekly";
    public const string EveryTwoWeeks = "Every 2 weeks";
    public const string Monthly = "Monthly";
    public const string Quarterly = "Every 3 months";
    public const string Yearly = "Yearly";
    public const string Once = "One time";
    public static readonly string[] All = [Monthly, Weekly, EveryTwoWeeks, Quarterly, Yearly, Once];

    /// <summary>The due date after <paramref name="due"/>. Monthly bills keep their day of the month (31st → 30th → 31st).</summary>
    public static DateTime Next(DateTime due, string repeats, int dayOfMonth)
    {
        int day = dayOfMonth > 0 ? dayOfMonth : due.Day;
        DateTime KeepDay(DateTime d) => new(d.Year, d.Month, Math.Min(day, DateTime.DaysInMonth(d.Year, d.Month)));
        return repeats switch
        {
            Weekly => due.AddDays(7),
            EveryTwoWeeks => due.AddDays(14),
            Monthly => KeepDay(due.AddMonths(1)),
            Quarterly => KeepDay(due.AddMonths(3)),
            Yearly => KeepDay(due.AddYears(1)),
            _ => due,
        };
    }

    /// <summary>What the bill costs per month on average (a weekly $10 bill is about $43.33 a month).</summary>
    public static decimal MonthlyCost(Bill b) => b.Repeats switch
    {
        Weekly => b.Amount * 52 / 12,
        EveryTwoWeeks => b.Amount * 26 / 12,
        Monthly => b.Amount,
        Quarterly => b.Amount / 3,
        Yearly => b.Amount / 12,
        _ => 0,
    };

    public static BillStatus Status(Bill b, DateTime today)
    {
        if (b.IsDone) return BillStatus.Paid;
        int days = (b.NextDue.Date - today.Date).Days;
        return days < 0 ? BillStatus.Overdue : days == 0 ? BillStatus.DueToday : days <= 7 ? BillStatus.DueSoon : BillStatus.Upcoming;
    }

    public static string StatusText(Bill b, DateTime today)
    {
        int days = (b.NextDue.Date - today.Date).Days;
        return Status(b, today) switch
        {
            BillStatus.Paid => $"Paid {b.LastPaid:MMM d}",
            BillStatus.Overdue => days == -1 ? "Overdue by 1 day" : $"Overdue by {-days} days",
            BillStatus.DueToday => "Due today",
            _ => days == 1 ? "Due tomorrow" : $"Due in {days} days",
        };
    }

    /// <summary>
    /// Marks the current due date as paid: records the payment as a transaction (unless told not to), remembers the
    /// date, and moves a repeating bill to its next due date.
    /// </summary>
    public static Transaction? MarkPaid(Bill b, DateTime paidOn, bool addTransaction = true)
    {
        Transaction? t = null;
        if (addTransaction)
        {
            t = new Transaction
            {
                Payee = b.Name,
                Category = b.Category,
                Account = b.Account,
                Date = paidOn.Date,
                Amount = -b.Amount,
                Note = $"Bill due {b.NextDue:MMM d}",
            };
            DataStore.Transactions.Add(t);
        }
        b.LastPaid = paidOn.Date;
        if (b.Repeats != Once) b.NextDue = Next(b.NextDue, b.Repeats, b.DueDay);
        DataStore.Bills.Update(b);
        return t;
    }
}

// ======================================================================
// Goals
// ======================================================================

public static class GoalMath
{
    /// <summary>For goals linked to an account, sets Saved to the account's balance (not saved to disk).</summary>
    public static void RefreshLinked()
    {
        foreach (var g in DataStore.Goals.Items.Where(g => g.IsLinked))
        {
            var account = DataStore.Accounts.Items.FirstOrDefault(a => string.Equals(a.Name, g.LinkedAccount, StringComparison.OrdinalIgnoreCase));
            if (account != null) g.Saved = Math.Max(0, AccountMath.Balance(account));
        }
    }

    /// <summary>The milestones (25%, 50%, 75%, 100%) a change in saved money crossed, if any.</summary>
    public static int? MilestoneCrossed(decimal before, decimal after, decimal target)
    {
        if (target <= 0 || after <= before) return null;
        foreach (int pct in new[] { 100, 75, 50, 25 })
        {
            decimal line = target * pct / 100;
            if (before < line && after >= line) return pct;
        }
        return null;
    }

    /// <summary>Months from this month to the target date's month (at least 1).</summary>
    public static int MonthsLeft(DateTime target, DateTime today) =>
        Math.Max(1, (target.Year - today.Year) * 12 + target.Month - today.Month);

    /// <summary>How much to save each month to reach the goal by its date, or null when there's no date or it's reached.</summary>
    public static decimal? MonthlyNeeded(Goal g, DateTime today)
    {
        if (g.IsReached || g.TargetDate is not DateTime date) return null;
        if (date.Date < today.Date) return g.Remaining;
        return Math.Ceiling(g.Remaining / MonthsLeft(date, today) * 100) / 100;
    }

    /// <summary>Adds (positive) or takes out (negative) money and keeps the history.</summary>
    public static void AddMoney(Goal g, decimal amount, DateTime date)
    {
        g.Saved = Math.Max(0, g.Saved + amount);
        g.History.Add(new MoneyEntry { Date = date.Date, Amount = amount });
        DataStore.Goals.Update(g);
    }
}

// ======================================================================
// Reports
// ======================================================================

public sealed record MonthTotals(DateTime Month, decimal Income, decimal Spent)
{
    public decimal Net => Income - Spent;
    /// <summary>Share of income not spent, or null with no income.</summary>
    public double? SavingsRate => Income <= 0 ? null : (double)((Income - Spent) / Income);
}

public static class Reports
{
    /// <summary>Totals for <paramref name="count"/> budget months ending with <paramref name="lastMonth"/> (oldest first).</summary>
    public static List<MonthTotals> Monthly(DateTime lastMonth, int count, IEnumerable<Transaction> transactions)
    {
        var last = Budgeting.MonthStart(lastMonth);
        var first = last.AddMonths(-(count - 1));
        var list = transactions.ToList();
        var result = new List<MonthTotals>();
        for (var m = first; m <= last; m = m.AddMonths(1))
        {
            var inMonth = list.Where(t => Budgeting.InMonth(t.Date, m)).ToList();
            result.Add(new MonthTotals(m,
                inMonth.Where(t => t.IsIncome).Sum(t => t.Amount),
                inMonth.Where(t => t.IsSpending).Sum(t => -t.Amount)));
        }
        return result;
    }

    /// <summary>Spending per category between two dates (inclusive), largest first. Subcategories roll up into their parent.</summary>
    public static List<(string Category, decimal Spent)> ByCategory(DateTime from, DateTime to, IEnumerable<Transaction> transactions, bool rollUp = true)
    {
        var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in transactions.Where(t => t.IsSpending && t.Date.Date >= from.Date && t.Date.Date <= to.Date))
            foreach (var (category, amount) in t.Parts())
            {
                string key = rollUp ? Categories.Root(category) : category;
                totals[key] = totals.GetValueOrDefault(key) - amount;
            }
        return totals.Select(kv => (kv.Key, kv.Value)).OrderByDescending(x => x.Value).ToList();
    }

    /// <summary>Spending per payee between two dates, largest first.</summary>
    public static List<(string Payee, decimal Spent, int Count)> ByPayee(DateTime from, DateTime to, IEnumerable<Transaction> transactions) =>
        transactions.Where(t => t.IsSpending && t.Date.Date >= from.Date && t.Date.Date <= to.Date)
            .GroupBy(t => t.Payee.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.First().Payee.Trim(), g.Sum(t => -t.Amount), g.Count()))
            .OrderByDescending(x => x.Item2).ToList();
}

/// <summary>The numbers for Reports → Year in review.</summary>
public sealed class YearReview
{
    public int Year { get; init; }
    public decimal Income { get; init; }
    public decimal Spent { get; init; }
    public decimal Net => Income - Spent;
    public double? SavingsRate => Income <= 0 ? null : (double)((Income - Spent) / Income);
    public int TransactionCount { get; init; }
    public List<MonthTotals> Months { get; init; } = [];
    public List<(string Category, decimal Spent)> TopCategories { get; init; } = [];
    public List<(string Payee, decimal Spent, int Count)> TopPayees { get; init; } = [];
    public Transaction? BiggestPurchase { get; init; }
    public decimal DebtPaid { get; init; }
    public decimal SavedToGoals { get; init; }
    public int BillsPaid { get; init; }

    /// <summary>Years that have any transactions, newest first (always includes this year).</summary>
    public static List<int> Years()
    {
        var years = DataStore.Transactions.Items.Select(t => t.Date.Year).Append(DateTime.Today.Year).Distinct().OrderByDescending(y => y).ToList();
        return years;
    }

    public static YearReview For(int year)
    {
        var from = new DateTime(year, 1, 1);
        var to = new DateTime(year, 12, 31);
        var tx = DataStore.Transactions.Items.Where(t => t.Date.Year == year).ToList();
        var months = Enumerable.Range(1, 12).Select(m => new DateTime(year, m, 1)).Select(m =>
        {
            var inMonth = tx.Where(t => t.Date.Month == m.Month).ToList();
            return new MonthTotals(m, inMonth.Where(t => t.IsIncome).Sum(t => t.Amount), inMonth.Where(t => t.IsSpending).Sum(t => -t.Amount));
        }).ToList();

        return new YearReview
        {
            Year = year,
            Income = tx.Where(t => t.IsIncome).Sum(t => t.Amount),
            Spent = tx.Where(t => t.IsSpending).Sum(t => -t.Amount),
            TransactionCount = tx.Count,
            Months = months,
            TopCategories = Reports.ByCategory(from, to, tx).Take(5).ToList(),
            TopPayees = Reports.ByPayee(from, to, tx).Take(5).ToList(),
            BiggestPurchase = tx.Where(t => t.IsSpending).OrderBy(t => t.Amount).FirstOrDefault(),
            DebtPaid = DataStore.Debts.Items.SelectMany(d => d.Payments).Where(p => p.Date.Year == year).Sum(p => p.Amount),
            SavedToGoals = DataStore.Goals.Items.SelectMany(g => g.History).Where(h => h.Date.Year == year).Sum(h => h.Amount),
            BillsPaid = DataStore.Bills.Items.Count(b => b.LastPaid?.Year == year),
        };
    }
}

// ======================================================================
// Subscriptions you might have forgotten
// ======================================================================

public sealed record SubscriptionGuess(string Payee, decimal Amount, string Repeats, DateTime LastCharge, DateTime NextDue, string Category, string Account, int Charges);

public static class SubscriptionFinder
{
    /// <summary>
    /// Looks for payees charged the same amount (within 10%) on a regular schedule — about every month, week or year —
    /// that aren't on the Bills page and that you haven't dismissed.
    /// </summary>
    public static List<SubscriptionGuess> Find(DateTime today)
    {
        var since = today.AddMonths(-14);
        var bills = DataStore.Bills.Items.Select(b => Normalize(b.Name)).ToList();
        var dismissed = DataStore.Prefs.DismissedSubscriptions.Select(Normalize).ToHashSet();
        var result = new List<SubscriptionGuess>();

        foreach (var group in DataStore.Transactions.Items
                     .Where(t => t.IsSpending && t.Date >= since && t.Payee.Trim().Length > 0)
                     .GroupBy(t => Normalize(t.Payee)))
        {
            string key = group.Key;
            if (key.Length < 2 || dismissed.Contains(key)) continue;
            if (bills.Any(b => b.Length > 0 && (b.Contains(key) || key.Contains(b)))) continue;

            var charges = group.OrderBy(t => t.Date).ToList();
            if (charges.Count < 2) continue;
            var amounts = charges.Select(t => -t.Amount).OrderBy(a => a).ToList();
            decimal median = amounts[amounts.Count / 2];
            if (median <= 0 || amounts.Any(a => Math.Abs(a - median) > median * 0.10m)) continue;

            var gaps = charges.Zip(charges.Skip(1), (a, b) => (b.Date - a.Date).TotalDays).OrderBy(g => g).ToList();
            double gap = gaps[gaps.Count / 2];
            string? repeats = gap switch
            {
                >= 6 and <= 8 => BillSchedule.Weekly,
                >= 13 and <= 16 => BillSchedule.EveryTwoWeeks,
                >= 26 and <= 35 => BillSchedule.Monthly,
                >= 85 and <= 98 => BillSchedule.Quarterly,
                >= 350 and <= 380 => BillSchedule.Yearly,
                _ => null,
            };
            if (repeats == null) continue;
            if (gaps.Any(g => Math.Abs(g - gap) > Math.Max(4, gap * 0.2))) continue;
            // Weekly and monthly charges need at least 3 to be convincing.
            if (charges.Count < (repeats is BillSchedule.Yearly or BillSchedule.Quarterly ? 2 : 3)) continue;

            var last = charges[^1];
            if ((today - last.Date).TotalDays > gap * 2 + 7) continue; // stopped a while ago
            var next = BillSchedule.Next(last.Date, repeats, last.Date.Day);
            while (next < today) next = BillSchedule.Next(next, repeats, last.Date.Day);
            result.Add(new SubscriptionGuess(last.Payee.Trim(), Math.Round(median, 2), repeats, last.Date, next,
                last.IsSplit ? "Subscriptions" : last.Category, last.Account, charges.Count));
        }
        return result.OrderByDescending(r => BillSchedule.MonthlyCost(new Bill { Amount = r.Amount, Repeats = r.Repeats })).ToList();
    }

    /// <summary>"NETFLIX.COM #1234" → "netflix.com": lower case, without trailing reference numbers.</summary>
    public static string Normalize(string payee)
    {
        var words = payee.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.TrimStart('#', '*').All(char.IsDigit))
            .Select(w => w.Trim('*', '#'));
        return string.Join(' ', words).Trim();
    }
}
