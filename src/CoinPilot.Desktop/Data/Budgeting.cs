namespace CoinPilot.Data;

/// <summary>One budget's numbers for one budget month.</summary>
public sealed record BudgetStatus(Budget Budget, decimal Spent, decimal Limit, decimal Carry)
{
    /// <summary>This month's amount plus anything rolled over from earlier months.</summary>
    public decimal Available => Limit + Carry;
    public decimal Left => Available - Spent;
    public bool IsOver => Spent > Available;
    /// <summary>0..1 (capped) for drawing the progress bar.</summary>
    public double Fraction => Available <= 0 ? (Spent > 0 ? 1 : 0) : Math.Min(1.0, (double)(Spent / Available));
    /// <summary>"Brush.Accent" under 85%, "Brush.Warn" from 85%, "Brush.Danger" when over.</summary>
    public string BrushKey => IsOver ? "Brush.Danger" : Fraction >= 0.85 ? "Brush.Warn" : "Brush.Accent";
}

/// <summary>Budget-month totals: what was spent, earned, and how the budgets are doing.</summary>
public sealed class MonthSummary
{
    public required DateTime Month { get; init; }
    public required IReadOnlyList<Transaction> Transactions { get; init; }
    public required IReadOnlyList<BudgetStatus> Budgets { get; init; }
    /// <summary>Spending per category this month (split transactions counted per piece).</summary>
    public required IReadOnlyDictionary<string, decimal> SpentByCategory { get; init; }

    public decimal Spent => Transactions.Where(t => t.IsSpending).Sum(t => -t.Amount);
    public decimal Income => Transactions.Where(t => t.IsIncome).Sum(t => t.Amount);
    public decimal Net => Income - Spent;

    public decimal Budgeted => Budgets.Sum(b => b.Limit);
    public decimal SpentInBudgets => Budgets.Sum(b => b.Spent);
    public decimal LeftToSpend => Budgets.Sum(b => b.Available) - SpentInBudgets;
    /// <summary>Spending in categories that have no budget (directly or through their parent).</summary>
    public decimal Unbudgeted => Spent - SpentInBudgets;
    public int OverCount => Budgets.Count(b => b.IsOver);
}

public static class Budgeting
{
    /// <summary>Day of the month budget months start on (Settings → Budget month). 1 = calendar months.</summary>
    public static int StartDay => Math.Clamp(DataStore.Prefs.MonthStartDay, 1, 28);

    /// <summary>First day of the budget month that contains <paramref name="d"/>.</summary>
    public static DateTime MonthStart(DateTime d)
    {
        int sd = StartDay;
        var start = new DateTime(d.Year, d.Month, sd);
        return d.Date >= start ? start : start.AddMonths(-1);
    }

    /// <summary>Last day of the budget month starting on <paramref name="start"/>.</summary>
    public static DateTime MonthEnd(DateTime start) => start.AddMonths(1).AddDays(-1);

    public static bool InMonth(DateTime date, DateTime month)
    {
        var start = MonthStart(month);
        return date.Date >= start && date.Date < start.AddMonths(1);
    }

    public static string Key(DateTime month) => MonthStart(month).ToString("yyyy-MM-dd");

    /// <summary>The budget's amount for one month: its single-month amount if it has one, otherwise the usual amount.</summary>
    public static decimal LimitFor(Budget b, DateTime month) =>
        b.Overrides.TryGetValue(Key(month), out var amount) ? amount : b.MonthlyLimit;

    private static Dictionary<string, decimal> SpendingByCategory(IEnumerable<Transaction> inMonth)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in inMonth.Where(t => t.IsSpending))
            foreach (var (category, amount) in t.Parts())
                result[category] = result.GetValueOrDefault(category) - amount;
        return result;
    }

    /// <summary>
    /// What counts against a budget: its own category, plus any subcategories that don't have a budget of their own.
    /// </summary>
    private static decimal SpentFor(Budget b, IReadOnlyDictionary<string, decimal> spent, ISet<string> budgeted)
    {
        decimal total = spent.GetValueOrDefault(b.Category);
        foreach (var child in Categories.Children(b.Category))
            if (!budgeted.Contains(child)) total += spent.GetValueOrDefault(child);
        return total;
    }

    public static MonthSummary Summarize(DateTime month, IEnumerable<Transaction> transactions, IEnumerable<Budget> budgets)
    {
        month = MonthStart(month);
        var all = transactions as IReadOnlyCollection<Transaction> ?? transactions.ToList();
        var inMonth = all.Where(t => InMonth(t.Date, month)).ToList();
        var spent = SpendingByCategory(inMonth);
        var budgetList = budgets.ToList();
        var budgeted = budgetList.Select(b => b.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var statuses = budgetList
            .OrderBy(b => b.Category, StringComparer.OrdinalIgnoreCase)
            .Select(b => new BudgetStatus(b, SpentFor(b, spent, budgeted), LimitFor(b, month),
                b.Rollover ? Carry(b, month, all, budgeted) : 0))
            .ToList();

        return new MonthSummary { Month = month, Transactions = inMonth, Budgets = statuses, SpentByCategory = spent };
    }

    public static MonthSummary Summarize(DateTime month) =>
        Summarize(month, DataStore.Transactions.Items, DataStore.Budgets.Items);

    /// <summary>Leftover (or overspending) carried into <paramref name="month"/> from the months before it.</summary>
    public static decimal Carry(Budget b, DateTime month, IReadOnlyCollection<Transaction> all, ISet<string> budgeted)
    {
        var start = MonthStart(b.StartMonth ?? (b.CreatedAt == default ? month : b.CreatedAt));
        month = MonthStart(month);
        decimal carry = 0;
        int guard = 0;
        for (var p = start; p < month && guard < 240; p = p.AddMonths(1), guard++)
        {
            var spent = SpendingByCategory(all.Where(t => InMonth(t.Date, p)));
            carry += LimitFor(b, p) - SpentFor(b, spent, budgeted);
        }
        return carry;
    }

    /// <summary>Spending per week of the budget month: days 1–7, 8–14, 15–21, 22–28, 29–end.</summary>
    public static decimal[] WeeklySpending(MonthSummary summary)
    {
        int days = (summary.Month.AddMonths(1) - summary.Month).Days;
        var weeks = new decimal[(days + 6) / 7];
        foreach (var t in summary.Transactions.Where(t => t.IsSpending))
            weeks[Math.Clamp((t.Date.Date - summary.Month).Days / 7, 0, weeks.Length - 1)] += -t.Amount;
        return weeks;
    }

    /// <summary>Average monthly income over the last few complete budget months (for budget templates).</summary>
    public static decimal AverageIncome(int months = 3)
    {
        var current = MonthStart(DateTime.Today);
        var values = Enumerable.Range(1, months)
            .Select(i => Summarize(current.AddMonths(-i)).Income)
            .Where(v => v > 0).ToList();
        return values.Count == 0 ? Summarize(current).Income : Math.Round(values.Average(), 2);
    }

    /// <summary>Average monthly spending per category over the last few complete budget months.</summary>
    public static Dictionary<string, decimal> AverageSpending(int months = 3)
    {
        var current = MonthStart(DateTime.Today);
        var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i <= months; i++)
            foreach (var (category, amount) in Summarize(current.AddMonths(-i)).SpentByCategory)
                totals[category] = totals.GetValueOrDefault(category) + amount;
        return totals.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value / months, 2), StringComparer.OrdinalIgnoreCase);
    }
}
