namespace CoinPilot.Data;

// ======================================================================
// Accounts
// ======================================================================

public static class AccountMath
{
    /// <summary>Starting balance plus this account's transactions dated after the balance date.</summary>
    public static decimal Balance(Account a, IEnumerable<Transaction> transactions) =>
        a.StartingBalance + transactions
            .Where(t => string.Equals(t.Account, a.Name, StringComparison.OrdinalIgnoreCase) && t.Date.Date > a.BalanceDate.Date)
            .Sum(t => t.Amount);

    public static decimal Balance(Account a) => Balance(a, DataStore.Transactions.Items);

    public static DateTime? LastActivity(Account a) =>
        DataStore.Transactions.Items
            .Where(t => string.Equals(t.Account, a.Name, StringComparison.OrdinalIgnoreCase))
            .Select(t => (DateTime?)t.Date).Max();

    /// <summary>Open accounts (assets minus card balances) minus everything on the Debts page.</summary>
    public static decimal NetWorth() =>
        DataStore.Accounts.Items.Where(a => !a.IsClosed).Sum(Balance)
        - DataStore.Debts.Items.Sum(d => Math.Max(0, d.Balance));

    /// <summary>Renames an account everywhere it's used (transactions and bills).</summary>
    public static void Rename(string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal) || oldName.Length == 0) return;
        foreach (var t in DataStore.Transactions.Items.Where(t => string.Equals(t.Account, oldName, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            t.Account = newName;
            DataStore.Transactions.Update(t);
        }
        foreach (var b in DataStore.Bills.Items.Where(b => string.Equals(b.Account, oldName, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            b.Account = newName;
            DataStore.Bills.Update(b);
        }
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
    /// <summary>Totals for <paramref name="count"/> months ending with <paramref name="lastMonth"/> (oldest first).</summary>
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
                inMonth.Where(t => t.Amount > 0).Sum(t => t.Amount),
                inMonth.Where(t => t.Amount < 0).Sum(t => -t.Amount)));
        }
        return result;
    }

    /// <summary>Spending per category between two dates (inclusive), largest first.</summary>
    public static List<(string Category, decimal Spent)> ByCategory(DateTime from, DateTime to, IEnumerable<Transaction> transactions) =>
        transactions
            .Where(t => t.Amount < 0 && t.Date.Date >= from.Date && t.Date.Date <= to.Date)
            .GroupBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.First().Category, g.Sum(t => -t.Amount)))
            .OrderByDescending(x => x.Item2)
            .ToList();
}
