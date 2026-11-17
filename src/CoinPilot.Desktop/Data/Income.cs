using System.Text.Json.Serialization;

namespace CoinPilot.Data;

/// <summary>
/// Money that comes in regularly: a paycheck, a side job, benefits, child support. Either the same amount every time
/// (added to Transactions automatically on payday, or confirmed with one click) or an amount that changes (WillowWallet
/// asks how much on payday).
/// </summary>
public sealed class IncomeSource : RecordBase
{
    public string Name { get; set; } = "";
    /// <summary>The amount each time; with <see cref="Varies"/>, a typical amount (0 when unknown).</summary>
    public decimal Amount { get; set; }
    /// <summary>The amount is different each time, so WillowWallet asks on payday.</summary>
    public bool Varies { get; set; }
    /// <summary>Same amount every time: add it to Transactions on payday without asking.</summary>
    public bool AutoAdd { get; set; } = true;
    public string Repeats { get; set; } = IncomeSchedule.EveryTwoWeeks;
    /// <summary>The next payday.</summary>
    public DateTime NextDate { get; set; } = DateTime.Today;
    /// <summary>Monthly and twice a month: the first day of the month it comes (0 = the day of NextDate).</summary>
    public int Day1 { get; set; }
    /// <summary>Twice a month: the second day (like the 1st and the 15th).</summary>
    public int Day2 { get; set; } = 15;
    public string Category { get; set; } = "Paycheck";
    public string Account { get; set; } = "";
    public DateTime? LastReceived { get; set; }
    public string Note { get; set; } = "";

    /// <summary>A one-time payment that has come in.</summary>
    [JsonIgnore] public bool IsDone => Repeats == IncomeSchedule.Once && LastReceived != null;

    /// <summary>Payday has come and WillowWallet needs an answer (an amount, or a confirmation).</summary>
    [JsonIgnore] public bool NeedsAsking => !IsDone && (Varies || !AutoAdd);
}

public static class IncomeSchedule
{
    public const string Weekly = "Weekly";
    public const string EveryTwoWeeks = "Every 2 weeks";
    public const string TwiceAMonth = "Twice a month";
    public const string Monthly = "Monthly";
    public const string Once = "One time";
    public static readonly string[] All = [EveryTwoWeeks, TwiceAMonth, Weekly, Monthly, Once];

    /// <summary>The payday after <paramref name="date"/>.</summary>
    public static DateTime Next(DateTime date, string repeats, int day1, int day2)
    {
        DateTime On(DateTime month, int day) => new(month.Year, month.Month, Math.Min(Math.Max(day, 1), DateTime.DaysInMonth(month.Year, month.Month)));
        switch (repeats)
        {
            case Weekly: return date.AddDays(7);
            case EveryTwoWeeks: return date.AddDays(14);
            case Monthly: return On(date.AddMonths(1), day1 > 0 ? day1 : date.Day);
            case TwiceAMonth:
            {
                int a = Math.Min(day1 > 0 ? day1 : 1, day2), b = Math.Max(day1 > 0 ? day1 : 1, day2);
                if (a == b) return On(date.AddMonths(1), a);
                // The next of the two days after this date: later this month, or the first one next month
                var second = On(date, b);
                return date.Date < second ? second : On(date.AddMonths(1), a);
            }
            default: return date;
        }
    }

    /// <summary>About how much comes in per month (every 2 weeks: 26 a year).</summary>
    public static decimal MonthlyAmount(IncomeSource s)
    {
        decimal each = s.Amount > 0 ? s.Amount : s.Varies ? Typical(s) : 0;
        return s.Repeats switch
        {
            Weekly => each * 52 / 12,
            EveryTwoWeeks => each * 26 / 12,
            TwiceAMonth => each * 2,
            Monthly => each,
            _ => 0,
        };
    }

    /// <summary>For an amount that changes: the average of the last few times it came in.</summary>
    public static decimal Typical(IncomeSource s)
    {
        var last = DataStore.Transactions.Items
            .Where(t => t.IsIncome && string.Equals(t.Payee, s.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.Date).Take(4).Select(t => t.Amount).ToList();
        return last.Count == 0 ? s.Amount : Math.Round(last.Average(), 2);
    }

    /// <summary>The paydays in [from, to].</summary>
    public static IEnumerable<DateTime> Paydays(IncomeSource s, DateTime from, DateTime to)
    {
        if (s.IsDone) yield break;
        var d = s.NextDate.Date;
        for (int i = 0; i < 400 && d <= to.Date; i++)
        {
            if (d >= from.Date) yield return d;
            if (s.Repeats == Once) yield break;
            d = Next(d, s.Repeats, s.Day1, s.Day2);
        }
    }

    public static string Describe(IncomeSource s) => s.Repeats switch
    {
        TwiceAMonth => $"Twice a month ({Ordinal(Math.Min(s.Day1 > 0 ? s.Day1 : 1, s.Day2))} and {Ordinal(Math.Max(s.Day1 > 0 ? s.Day1 : 1, s.Day2))})",
        _ => s.Repeats,
    };

    public static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
}

/// <summary>Recording income: on payday, automatically or after asking.</summary>
public static class Paydays
{
    /// <summary>Expected income per month from all repeating income (0 when none is set up).</summary>
    public static decimal ExpectedMonthly() => DataStore.Income.Items.Where(s => !s.IsDone).Sum(IncomeSchedule.MonthlyAmount);

    /// <summary>Income whose payday has come and that needs an amount or a confirmation.</summary>
    public static List<IncomeSource> Asking(DateTime today) =>
        DataStore.Income.Items.Where(s => s.NeedsAsking && s.NextDate.Date <= today.Date).OrderBy(s => s.NextDate).ToList();

    /// <summary>Adds the income to Transactions and moves the source to its next payday.</summary>
    public static Transaction Receive(IncomeSource s, decimal amount, DateTime date, string? account = null)
    {
        var t = new Transaction
        {
            Payee = s.Name,
            Category = s.Category.Length > 0 ? s.Category : "Other income",
            Account = account ?? (s.Account.Length > 0 ? s.Account : DataStore.AccountChoices()[0]),
            Date = date.Date,
            Amount = Math.Abs(amount),
            Note = s.Varies ? "" : "Added on payday",
        };
        DataStore.Transactions.Add(t);
        Advance(s, date);
        return t;
    }

    /// <summary>Moves to the next payday without recording anything (it didn't come, or it was recorded another way).</summary>
    public static void Advance(IncomeSource s, DateTime received)
    {
        s.LastReceived = received.Date;
        if (s.Repeats != IncomeSchedule.Once) s.NextDate = IncomeSchedule.Next(s.NextDate, s.Repeats, s.Day1, s.Day2);
        DataStore.Income.Update(s);
    }

    /// <summary>
    /// Same-amount income set to add itself: records every payday up to today (catching up after days away).
    /// Returns what was added.
    /// </summary>
    public static List<Transaction> AddDue(DateTime today)
    {
        var added = new List<Transaction>();
        using (DataStore.Batch())
        {
            foreach (var s in DataStore.Income.Items.Where(s => !s.Varies && s.AutoAdd && s.Amount > 0).ToList())
                for (int i = 0; i < 60 && !s.IsDone && s.NextDate.Date <= today.Date; i++)
                    added.Add(Receive(s, s.Amount, s.NextDate));
        }
        return added;
    }
}
