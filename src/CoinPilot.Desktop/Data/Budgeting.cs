namespace CoinPilot.Data;

/// <summary>One budget's numbers for one month.</summary>
public sealed record BudgetStatus(Budget Budget, decimal Spent)
{
    public decimal Limit => Budget.MonthlyLimit;
    public decimal Left => Limit - Spent;
    public bool IsOver => Spent > Limit;
    /// <summary>0..1 (capped) for drawing the progress bar.</summary>
    public double Fraction => Limit <= 0 ? (Spent > 0 ? 1 : 0) : Math.Min(1.0, (double)(Spent / Limit));
    /// <summary>"Brush.Accent" under 85%, "Brush.Warn" from 85%, "Brush.Danger" when over.</summary>
    public string BrushKey => IsOver ? "Brush.Danger" : Fraction >= 0.85 ? "Brush.Warn" : "Brush.Accent";
}

/// <summary>Month totals: what was spent, earned, and how the budgets are doing.</summary>
public sealed class MonthSummary
{
    public required DateTime Month { get; init; }
    public required IReadOnlyList<Transaction> Transactions { get; init; }
    public required IReadOnlyList<BudgetStatus> Budgets { get; init; }

    public decimal Spent => Transactions.Where(t => t.Amount < 0).Sum(t => -t.Amount);
    public decimal Income => Transactions.Where(t => t.Amount > 0).Sum(t => t.Amount);
    public decimal Net => Income - Spent;

    public decimal Budgeted => Budgets.Sum(b => b.Limit);
    public decimal SpentInBudgets => Budgets.Sum(b => b.Spent);
    public decimal LeftToSpend => Budgeted - SpentInBudgets;
    /// <summary>Spending in categories that have no budget.</summary>
    public decimal Unbudgeted => Spent - SpentInBudgets;
    public int OverCount => Budgets.Count(b => b.IsOver);
}

public static class Budgeting
{
    public static DateTime MonthStart(DateTime d) => new(d.Year, d.Month, 1);

    public static bool InMonth(DateTime date, DateTime month) => date.Year == month.Year && date.Month == month.Month;

    public static MonthSummary Summarize(DateTime month, IEnumerable<Transaction> transactions, IEnumerable<Budget> budgets)
    {
        month = MonthStart(month);
        var inMonth = transactions.Where(t => InMonth(t.Date, month)).ToList();
        var spentByCategory = inMonth.Where(t => t.Amount < 0)
            .GroupBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(t => -t.Amount), StringComparer.OrdinalIgnoreCase);

        var statuses = budgets
            .OrderBy(b => b.Category, StringComparer.OrdinalIgnoreCase)
            .Select(b => new BudgetStatus(b, spentByCategory.GetValueOrDefault(b.Category)))
            .ToList();

        return new MonthSummary { Month = month, Transactions = inMonth, Budgets = statuses };
    }

    public static MonthSummary Summarize(DateTime month) =>
        Summarize(month, DataStore.Transactions.Items, DataStore.Budgets.Items);

    /// <summary>Spending per week of the month: days 1–7, 8–14, 15–21, 22–28, 29–end.</summary>
    public static decimal[] WeeklySpending(MonthSummary summary)
    {
        int days = DateTime.DaysInMonth(summary.Month.Year, summary.Month.Month);
        var weeks = new decimal[(days + 6) / 7];
        foreach (var t in summary.Transactions.Where(t => t.Amount < 0))
            weeks[(t.Date.Day - 1) / 7] += -t.Amount;
        return weeks;
    }
}
