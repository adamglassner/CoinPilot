using System.Text.Json.Serialization;

namespace CoinPilot.Data;

/// <summary>Fields every saved record has.</summary>
public abstract class RecordBase
{
    public string Id { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// One purchase, bill payment, paycheck or transfer. Amount is negative for money out of <see cref="Account"/>,
/// positive for money in. A transfer moves money from <see cref="Account"/> to <see cref="TransferAccount"/> and
/// doesn't count as spending or income.
/// </summary>
public sealed class Transaction : RecordBase
{
    public string Payee { get; set; } = "";
    public string Category { get; set; } = "";
    public string Account { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public string Note { get; set; } = "";
    /// <summary>When set, the amount is divided across these categories (each Amount positive; they add up to the total).</summary>
    public List<Split> Splits { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    /// <summary>File name of an attached receipt in the data\receipts folder.</summary>
    public string Receipt { get; set; } = "";
    /// <summary>Destination account for a transfer; empty for ordinary transactions.</summary>
    public string TransferAccount { get; set; } = "";
    /// <summary>Bank's id for an imported transaction (OFX FITID), used to skip duplicates.</summary>
    public string ImportId { get; set; } = "";

    [JsonIgnore] public bool IsTransfer => TransferAccount.Length > 0;
    [JsonIgnore] public bool IsIncome => Amount > 0 && !IsTransfer;
    [JsonIgnore] public bool IsSpending => Amount < 0 && !IsTransfer;
    [JsonIgnore] public bool IsSplit => Splits.Count > 0;
    /// <summary>Always positive.</summary>
    [JsonIgnore] public decimal Size => Math.Abs(Amount);

    /// <summary>
    /// The category pieces of this transaction with signed amounts: one piece normally, one per split when split,
    /// none for transfers (they're neither spending nor income).
    /// </summary>
    public IEnumerable<(string Category, decimal Amount)> Parts()
    {
        if (IsTransfer) yield break;
        if (Splits.Count == 0)
        {
            yield return (Category, Amount);
            yield break;
        }
        int sign = Amount < 0 ? -1 : 1;
        foreach (var s in Splits) yield return (s.Category, sign * Math.Abs(s.Amount));
    }

    /// <summary>"Groceries", "Split (3)" or "Transfer".</summary>
    [JsonIgnore] public string CategoryLabel => IsTransfer ? "Transfer" : IsSplit ? $"Split ({Splits.Count})" : Category;
}

public sealed class Split
{
    public string Category { get; set; } = "";
    public decimal Amount { get; set; }
}

/// <summary>
/// A monthly spending limit for one category. Applies to every month unless a month has its own amount in
/// <see cref="Overrides"/>. With <see cref="Rollover"/>, leftover money (or overspending) carries into the next month.
/// </summary>
public sealed class Budget : RecordBase
{
    public string Category { get; set; } = "";
    public decimal MonthlyLimit { get; set; }
    public bool Rollover { get; set; }
    /// <summary>Amounts for single months, keyed by the month's start date ("yyyy-MM-dd").</summary>
    public Dictionary<string, decimal> Overrides { get; set; } = [];
    /// <summary>First budget month that counts for rollover. Defaults to the month the budget was created.</summary>
    public DateTime? StartMonth { get; set; }
}

/// <summary>
/// A bank account, card or wallet. Its balance is <see cref="StartingBalance"/> as of <see cref="BalanceDate"/>,
/// plus every transaction in this account dated after that day. Credit cards store what's owed as a negative number.
/// </summary>
public sealed class Account : RecordBase
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = AccountTypes.Checking;
    public string Institution { get; set; } = "";
    public decimal StartingBalance { get; set; }
    public DateTime BalanceDate { get; set; } = DateTime.Today;
    public bool IsClosed { get; set; }

    [JsonIgnore] public bool IsLiability => Type == AccountTypes.CreditCard;
}

public static class AccountTypes
{
    public const string Checking = "Checking";
    public const string Savings = "Savings";
    public const string CreditCard = "Credit card";
    public const string Cash = "Cash";
    public const string Investment = "Investment";
    public const string Other = "Other";
    public static readonly string[] All = [Checking, Savings, CreditCard, Cash, Investment, Other];
}

/// <summary>Money owed on a loan or card, with its interest rate, minimum payment and payments made.</summary>
public sealed class Debt : RecordBase
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = DebtTypes.CreditCard;
    /// <summary>What's owed right now.</summary>
    public decimal Balance { get; set; }
    /// <summary>The balance when it was added (for "paid off so far").</summary>
    public decimal OriginalBalance { get; set; }
    /// <summary>Yearly interest rate in percent (APR), e.g. 22.99.</summary>
    public decimal InterestRate { get; set; }
    public decimal MinimumPayment { get; set; }
    public string Note { get; set; } = "";
    public List<MoneyEntry> Payments { get; set; } = [];

    [JsonIgnore] public decimal PaidOff => Math.Max(0, OriginalBalance - Balance);
    [JsonIgnore] public bool IsPaidOff => Balance <= 0;
}

public static class DebtTypes
{
    public const string CreditCard = "Credit card";
    public static readonly string[] All = [CreditCard, "Student loan", "Car loan", "Mortgage", "Personal loan", "Medical", "Other"];
}

/// <summary>A dated amount: a debt payment, or money added to (positive) / taken from (negative) a goal.</summary>
public sealed class MoneyEntry
{
    public DateTime Date { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
}

/// <summary>A bill or subscription that comes due on a schedule.</summary>
public sealed class Bill : RecordBase
{
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime NextDue { get; set; } = DateTime.Today;
    /// <summary>Day of the month a monthly/quarterly/yearly bill is due (so the 31st stays the 31st after a short month).</summary>
    public int DueDay { get; set; }
    public string Repeats { get; set; } = BillSchedule.Monthly;
    public string Category { get; set; } = "Utilities";
    public string Account { get; set; } = "";
    public bool AutoPay { get; set; }
    public DateTime? LastPaid { get; set; }
    public string Note { get; set; } = "";

    /// <summary>A one-time bill that has been paid. Repeating bills move to their next due date instead.</summary>
    [JsonIgnore] public bool IsDone => Repeats == BillSchedule.Once && LastPaid != null;
}

/// <summary>Something to save toward. When linked to an account, the account's balance is what's saved.</summary>
public sealed class Goal : RecordBase
{
    public string Name { get; set; } = "";
    public decimal Target { get; set; }
    public decimal Saved { get; set; }
    public DateTime? TargetDate { get; set; }
    public string Note { get; set; } = "";
    public string LinkedAccount { get; set; } = "";
    public List<MoneyEntry> History { get; set; } = [];

    [JsonIgnore] public bool IsLinked => LinkedAccount.Length > 0;
    [JsonIgnore] public bool IsReached => Target > 0 && Saved >= Target;
    [JsonIgnore] public decimal Remaining => Math.Max(0, Target - Saved);
    [JsonIgnore] public double Fraction => Target <= 0 ? 0 : Math.Min(1.0, (double)(Saved / Target));
}

/// <summary>A category you can pick for transactions. A subcategory has a <see cref="Parent"/>.</summary>
public sealed class CategoryDef : RecordBase
{
    public string Name { get; set; } = "";
    /// <summary>"Spending" or "Income".</summary>
    public string Kind { get; set; } = CategoryKinds.Spending;
    public string Parent { get; set; } = "";
    public bool IsHidden { get; set; }
    public int Order { get; set; }
}

public static class CategoryKinds
{
    public const string Spending = "Spending";
    public const string Income = "Income";
}

/// <summary>"When the payee contains X, use category Y."</summary>
public sealed class CategoryRule : RecordBase
{
    public string Contains { get; set; } = "";
    public string Category { get; set; } = "";
}
