namespace CoinPilot.Data;

/// <summary>Built-in categories and accounts. (Editing them is a planned feature.)</summary>
public static class Categories
{
    public static readonly string[] Spending =
    [
        "Groceries", "Dining out", "Housing", "Utilities", "Transportation", "Shopping",
        "Entertainment", "Health", "Personal care", "Kids", "Pets", "Travel", "Gifts", "Subscriptions", "Other",
    ];

    public static readonly string[] Income = ["Paycheck", "Side income", "Refund", "Gift received", "Other income"];

    public static readonly string[] Accounts = ["Checking", "Savings", "Credit card", "Cash"];

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
        _ => 100,
    };
}
