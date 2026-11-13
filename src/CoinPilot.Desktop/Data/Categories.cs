namespace CoinPilot.Data;

/// <summary>Built-in categories and accounts. (Editing them is a planned feature.)</summary>
public static class Categories
{
    public static readonly string[] Spending =
    [
        "Groceries", "Dining out", "Housing", "Utilities", "Transportation", "Shopping",
        "Entertainment", "Health", "Personal care", "Kids", "Pets", "Travel", "Gifts", "Subscriptions",
        "Debt payments", "Savings", "Other",
    ];

    public static readonly string[] Income = ["Paycheck", "Side income", "Refund", "Gift received", "Other income"];

    /// <summary>Offered for transactions until you set up your own accounts on the Accounts page.</summary>
    public static readonly string[] Accounts = ["Checking", "Savings", "Credit card", "Cash"];

    /// <summary>Category used for debt payments recorded from the Debts page.</summary>
    public const string DebtPayments = "Debt payments";

    /// <summary>A suggested starting amount when someone budgets a category for the first time.</summary>
    public static decimal SuggestedLimit(string category) => category switch
    {
        "Housing" => 1500,
        "Groceries" => 600,
        "Utilities" => 250,
        "Transportation" => 300,
        "Dining out" => 250,
        "Shopping" => 200,
        "Entertainment" => 150,
        "Health" => 100,
        "Subscriptions" => 60,
        "Debt payments" => 300,
        "Savings" => 200,
        _ => 100,
    };
}
