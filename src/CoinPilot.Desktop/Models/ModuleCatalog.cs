namespace CoinPilot.Models;

/// <summary>
/// Every section of CoinPilot. Sections that work (IsComingSoon = false) have their own page;
/// everything else uses the generic ComingSoonView built from this data.
/// Add a module here and it appears in the sidebar automatically.
/// </summary>
public static class ModuleCatalog
{
    public static readonly ModuleInfo Dashboard = new()
    {
        Key = "dashboard", Title = "Dashboard", Glyph = "", IsComingSoon = false,
        Tagline = "Where your money stands this month.",
    };

    public static readonly IReadOnlyList<ModuleInfo> Sidebar =
    [
        Dashboard,
        new()
        {
            Key = "transactions", Title = "Transactions", Glyph = "", IsComingSoon = false,
            Tagline = "Every dollar in and out, in one list.",
            Description = "Add spending and income by hand or import it from your bank, then sort it into categories. Search, filter and split transactions, and attach receipts.",
            PrimaryAction = "Add transaction",
            Kpis = ["Spent this month", "Income this month", "Uncategorized", "Transactions"],
            Columns = ["Merchant", "Category", "Account", "Date", "Amount"],
            Features =
            [
                "Quick add for spending and income",
                "Import bank and card statements (CSV, OFX, QFX)",
                "Categories and subcategories you can edit",
                "Rules that categorize recurring merchants automatically",
                "Split one transaction across several categories",
                "Search, filters and tags",
                "Receipt photos from the mobile app",
                "Export to CSV",
            ],
        },
        new()
        {
            Key = "budgets", Title = "Budgets", Glyph = "", IsComingSoon = false,
            Tagline = "Give every dollar a job and see how each category is doing.",
            Description = "Set a monthly amount for each category and watch progress bars fill as you spend. CoinPilot warns you before a category runs out and shows what's left to spend for the rest of the month.",
            PrimaryAction = "New budget",
            Kpis = ["Budgeted", "Spent", "Left to spend", "Over budget"],
            Columns = ["Category", "Budgeted", "Spent", "Left", "Progress"],
            Features =
            [
                "Monthly budgets per category",
                "Left to spend for the month, at a glance",
                "Warnings at 85% and when a category goes over",
                "Roll leftover money into next month",
                "Budget templates (50/30/20, zero-based)",
                "Copy last month's budget in one click",
            ],
        },
        new()
        {
            Key = "goals", Title = "Goals", Glyph = "",
            Tagline = "Save toward the things that matter.",
            Description = "Create savings goals like an emergency fund, a trip or a new car. Set a target and a date and CoinPilot works out how much to put away each month.",
            PrimaryAction = "New goal",
            Kpis = ["Active goals", "Saved so far", "Monthly contribution", "On track"],
            Columns = ["Goal", "Target", "Saved", "Target date", "Status"],
            Features =
            [
                "Savings goals with a target amount and date",
                "Suggested monthly contribution",
                "Progress rings and milestones",
                "Debt payoff plans (snowball and avalanche)",
                "Link a goal to a savings account",
            ],
        },
        new()
        {
            Key = "accounts", Title = "Accounts", Glyph = "",
            Tagline = "Checking, savings, credit cards and cash, together.",
            Description = "Keep track of every account's balance and see your net worth. Add accounts manually, or import statements to keep balances up to date.",
            PrimaryAction = "Add account",
            Kpis = ["Net worth", "Cash", "Credit card balances", "Accounts"],
            Columns = ["Account", "Type", "Institution", "Balance", "Updated"],
            Features =
            [
                "Checking, savings, credit card, cash and loan accounts",
                "Net worth over time",
                "Statement import to update balances",
                "Transfers between accounts that don't count as spending",
                "Hide closed accounts",
            ],
        },
        new()
        {
            Key = "bills", Title = "Bills", Glyph = "",
            Tagline = "Never miss a due date.",
            Description = "List your bills and subscriptions with their due dates and amounts. CoinPilot reminds you before they're due and shows how much is still coming out this month.",
            PrimaryAction = "Add bill",
            Kpis = ["Due this week", "Due this month", "Subscriptions", "Monthly total"],
            Columns = ["Bill", "Amount", "Due", "Repeats", "Status"],
            Features =
            [
                "Bills and subscriptions with due dates",
                "Reminders before a bill is due",
                "Calendar view of the month",
                "Mark as paid (creates the transaction)",
                "Spot subscriptions you forgot about",
            ],
        },
        new()
        {
            Key = "reports", Title = "Reports", Glyph = "",
            Tagline = "See where your money goes over time.",
            Description = "Charts of spending by category, income versus spending, and month-to-month trends. Pick any date range and export what you see.",
            PrimaryAction = "Run report",
            Columns = ["Report", "Period", "Last run", "Format"],
            Features =
            [
                "Spending by category",
                "Income vs. spending by month",
                "Trends and averages",
                "Net worth over time",
                "Year in review",
                "Export to PDF and CSV",
            ],
        },
        new()
        {
            Key = "sync", Title = "Sync & Devices", Glyph = "",
            Tagline = "Your budget on your PC and your phone, always up to date.",
            Description = "Sign in on each device and CoinPilot keeps your budgets, transactions and goals in sync between Windows and the CoinPilot mobile app. Add a purchase on your phone at the store and it's on your PC when you get home.",
            PrimaryAction = "Add a device",
            Kpis = ["Devices", "Last sync", "Pending changes", "Sync status"],
            Columns = ["Device", "Platform", "Last seen", "Status"],
            Features =
            [
                "Sync between Windows and the CoinPilot mobile app",
                "Mobile app for iPhone and Android",
                "Works offline and syncs when you reconnect",
                "Share a budget with a partner or family",
                "Choose where your data is stored",
            ],
        },
    ];

    public static ModuleInfo? Find(string key) => Sidebar.FirstOrDefault(m => m.Key == key);
}
