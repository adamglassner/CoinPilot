using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CoinPilot.Data;
using CoinPilot.Models;
using CoinPilot.Views;

namespace CoinPilot.Services;

/// <summary>
/// Headless checks run by CI: <c>CoinPilot.exe --self-test result.txt</c>.
/// Uses a throwaway data folder, opens every page in both themes (empty and with data), exercises the
/// add/edit dialogs, the budget math and saving to disk. Exit code = number of failures.
/// </summary>
public static class SelfTest
{
    public static string? OutputFile { get; set; }
    public static bool IsRunning => OutputFile != null;

    public static readonly string TempDataFolder =
        Path.Combine(Path.GetTempPath(), "coinpilot-selftest-" + Guid.NewGuid().ToString("N")[..8]);

    private static readonly StringBuilder Report = new();
    private static int _failed;

    /// <summary>Called by App's unhandled-exception handler while the self-test runs.</summary>
    public static void RecordUnhandled(Exception ex)
    {
        _failed++;
        Report.AppendLine($"FAIL  Unhandled exception — {ex}");
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (!ok) _failed++;
        Report.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  — " + detail : "")}");
    }

    private static async Task Step(string name, Action action)
    {
        try
        {
            action();
            // Let layout, data binding and rendering run so XAML errors surface here.
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Application.Current.MainWindow?.UpdateLayout();
            Check(name, true);
        }
        catch (Exception ex)
        {
            Check(name, false, ex.ToString());
        }
    }

    private static async Task AllPages(MainWindow window, string label)
    {
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            await Step($"[{label}/{theme}] Apply theme", () => App.ApplyTheme(theme));
            foreach (var module in ModuleCatalog.Sidebar)
            {
                await Step($"[{label}/{theme}] Page: {module.Title}", () => window.GoTo(module.Key));
                Check($"[{label}/{theme}] {module.Title} is selected in the sidebar",
                    module.IsSelected && ModuleCatalog.Sidebar.Count(m => m.IsSelected) == 1);
            }
            await Step($"[{label}/{theme}] Page: Settings", window.ShowSettings);
            Check($"[{label}/{theme}] Settings clears the sidebar selection", ModuleCatalog.Sidebar.All(m => !m.IsSelected));
        }
    }

    private static async Task Dialog(string name, Func<Window> make)
    {
        await Step(name, () =>
        {
            var dialog = make();
            dialog.Show();
            dialog.UpdateLayout();
            dialog.Close();
        });
    }

    public static async Task<int> RunAsync(MainWindow window)
    {
        Report.AppendLine($"CoinPilot {App.VersionText} self-test {DateTime.Now:u}");
        Report.AppendLine($"Data folder: {DataStore.Folder}");
        Check("Self-test uses a temp data folder", DataStore.Folder == TempDataFolder, DataStore.Folder);

        // ----- Money parsing / formatting -----
        Check("Parse 86.42", Money.TryParse("86.42", out var a1) && a1 == 86.42m);
        Check("Parse $1,284.50", Money.TryParse(" $1,284.50 ", out var a2) && a2 == 1284.50m);
        Check("Parse 12", Money.TryParse("12", out var a3) && a3 == 12m);
        Check("Reject letters", !Money.TryParse("12abc", out _));
        Check("Reject negative", !Money.TryParse("-5", out _));
        Check("Reject 3 decimals", !Money.TryParse("1.005", out _));
        Check("Reject empty", !Money.TryParse("  ", out _));
        Check("Format", Money.Format(1284.5m) == "$1,284.50" && Money.Format(-3m, cents: false) == "−$3", Money.Format(1284.5m));
        Check("Signed", Money.Signed(-86.42m) == "−$86.42" && Money.Signed(2050m) == "+$2,050.00", Money.Signed(-86.42m));

        // ----- Empty: every page with no data -----
        await AllPages(window, "empty");

        // ----- Add data -----
        var month = Budgeting.MonthStart(DateTime.Today);
        var lastMonth = month.AddMonths(-1);
        await Step("Add transactions and budgets", () =>
        {
            DataStore.Transactions.Add(new Transaction { Payee = "Fresh Thyme Market", Category = "Groceries", Account = "Checking", Date = month.AddDays(1), Amount = -86.42m });
            DataStore.Transactions.Add(new Transaction { Payee = "Aldi", Category = "Groceries", Account = "Credit card", Date = month, Amount = -120.00m });
            DataStore.Transactions.Add(new Transaction { Payee = "Taco Bros", Category = "Dining out", Account = "Credit card", Date = month.AddDays(9), Amount = -275.00m, Note = "Birthday dinner" });
            DataStore.Transactions.Add(new Transaction { Payee = "Shell", Category = "Transportation", Account = "Credit card", Date = month.AddDays(20), Amount = -41.10m });
            DataStore.Transactions.Add(new Transaction { Payee = "Paycheck", Category = "Paycheck", Account = "Checking", Date = month, Amount = 2050.00m });
            DataStore.Transactions.Add(new Transaction { Payee = "Last month's rent", Category = "Housing", Account = "Checking", Date = lastMonth.AddDays(2), Amount = -1500m });
            DataStore.Budgets.Add(new Budget { Category = "Groceries", MonthlyLimit = 600 });
            DataStore.Budgets.Add(new Budget { Category = "Dining out", MonthlyLimit = 250 });
        });

        // ----- Budget math -----
        var s = Budgeting.Summarize(month);
        Check("Month has 5 transactions (last month's excluded)", s.Transactions.Count == 5, s.Transactions.Count.ToString());
        Check("Spent = 522.52", s.Spent == 522.52m, s.Spent.ToString());
        Check("Income = 2050", s.Income == 2050m, s.Income.ToString());
        Check("Budgeted = 850", s.Budgeted == 850m, s.Budgeted.ToString());
        Check("Spent in budgets = 481.42", s.SpentInBudgets == 481.42m, s.SpentInBudgets.ToString());
        Check("Left to spend = 368.58", s.LeftToSpend == 368.58m, s.LeftToSpend.ToString());
        Check("Unbudgeted = 41.10 (Transportation)", s.Unbudgeted == 41.10m, s.Unbudgeted.ToString());
        var dining = s.Budgets.Single(b => b.Budget.Category == "Dining out");
        Check("Dining out is over budget by 25", dining.IsOver && dining.Left == -25m && dining.BrushKey == "Brush.Danger");
        var groceries = s.Budgets.Single(b => b.Budget.Category == "Groceries");
        Check("Groceries under budget", !groceries.IsOver && groceries.BrushKey == "Brush.Accent", groceries.Fraction.ToString("0.00"));
        Check("One budget over", s.OverCount == 1);
        var weeks = Budgeting.WeeklySpending(s);
        Check("Weekly spending split by day of month", weeks[0] == 206.42m && weeks[1] == 275m && weeks[2] == 41.10m, string.Join(", ", weeks));
        Check("Last month's spending stays in last month", Budgeting.Summarize(lastMonth).Spent == 1500m);

        // ----- Saved to disk -----
        var reloadedT = new JsonStore<Transaction>(DataStore.Folder, "transactions");
        var reloadedB = new JsonStore<Budget>(DataStore.Folder, "budgets");
        Check("Transactions saved to disk", reloadedT.Items.Count == 6, reloadedT.Items.Count.ToString());
        Check("Budgets saved to disk", reloadedB.Items.Count == 2, reloadedB.Items.Count.ToString());
        var dinner = reloadedT.Items.Single(t => t.Payee == "Taco Bros");
        Check("Fields survive a reload", dinner.Amount == -275m && dinner.Note == "Birthday dinner" && dinner.Date == month.AddDays(9) && dinner.Id.Length == 32);

        // ----- Pages with data, both months -----
        MonthSwitcher.Current = month;
        await AllPages(window, "data");
        await Step("Transactions page: last month", () => { MonthSwitcher.Current = lastMonth; window.GoTo("transactions"); });
        await Step("Budgets page: last month", () => window.GoTo("budgets"));
        MonthSwitcher.Current = month;

        // ----- Dialogs -----
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            App.ApplyTheme(theme);
            await Dialog($"[{theme}] Add transaction dialog", () => new TransactionDialog(null, DateTime.Today));
            await Dialog($"[{theme}] Edit transaction dialog", () => new TransactionDialog(DataStore.Transactions.Items[0]));
            await Dialog($"[{theme}] Edit income dialog", () => new TransactionDialog(DataStore.Transactions.Items.First(t => t.IsIncome)));
            await Dialog($"[{theme}] New budget dialog", () => new BudgetDialog(null, "Transportation"));
            await Dialog($"[{theme}] Edit budget dialog", () => new BudgetDialog(DataStore.Budgets.Items[0]));
        }

        // ----- Edit + delete refresh the open page -----
        await Step("Edit and delete with Transactions page open", () =>
        {
            window.GoTo("transactions");
            var shell = DataStore.Transactions.Items.Single(t => t.Payee == "Shell");
            shell.Amount = -50m;
            DataStore.Transactions.Update(shell);
            DataStore.Transactions.Delete(shell);
        });
        await Step("Delete a budget with Budgets page open", () =>
        {
            window.GoTo("budgets");
            DataStore.Budgets.Delete(DataStore.Budgets.Items.Single(b => b.Category == "Dining out"));
        });
        Check("Delete persisted", new JsonStore<Transaction>(DataStore.Folder, "transactions").Items.Count == 5
                                  && new JsonStore<Budget>(DataStore.Folder, "budgets").Items.Count == 1);

        // ----- Catalog / navigation -----
        string[] working = ["dashboard", "transactions", "budgets"];
        Check("Dashboard, Transactions and Budgets work; the rest are coming soon",
            ModuleCatalog.Sidebar.All(m => m.IsComingSoon == !working.Contains(m.Key)));
        Check("Every coming-soon section lists planned features",
            ModuleCatalog.Sidebar.Where(m => m.IsComingSoon).All(m => m.Features.Length > 0 && m.Description.Length > 0));
        Check("Section keys are unique",
            ModuleCatalog.Sidebar.Select(m => m.Key).Distinct().Count() == ModuleCatalog.Sidebar.Count);
        Check("Version is set", App.VersionText != "0.0.0", App.VersionText);
        await Step("Toast", () => window.ShowToast("Self-test toast"));
        await Step("Unknown page falls back to the Dashboard", () => window.GoTo("no-such-page"));
        Check("Dashboard selected after unknown page", ModuleCatalog.Dashboard.IsSelected);

        Report.AppendLine(_failed == 0 ? "ALL PASSED" : $"{_failed} FAILED");
        try { File.WriteAllText(OutputFile!, Report.ToString()); } catch { /* ignore */ }
        try { Directory.Delete(TempDataFolder, recursive: true); } catch { /* ignore */ }
        return _failed;
    }
}
