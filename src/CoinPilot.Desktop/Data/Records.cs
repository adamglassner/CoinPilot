using System.Text.Json.Serialization;

namespace CoinPilot.Data;

/// <summary>Fields every saved record has.</summary>
public abstract class RecordBase
{
    public string Id { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>One purchase, bill payment, paycheck… Amount is negative for money out, positive for money in.</summary>
public sealed class Transaction : RecordBase
{
    public string Payee { get; set; } = "";
    public string Category { get; set; } = "";
    public string Account { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public string Note { get; set; } = "";

    [JsonIgnore] public bool IsIncome => Amount > 0;
    /// <summary>Always positive.</summary>
    [JsonIgnore] public decimal Size => Math.Abs(Amount);
}

/// <summary>A monthly spending limit for one category. Applies to every month.</summary>
public sealed class Budget : RecordBase
{
    public string Category { get; set; } = "";
    public decimal MonthlyLimit { get; set; }
}
