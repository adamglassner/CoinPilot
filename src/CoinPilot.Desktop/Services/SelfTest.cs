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
    /// <summary>True for any automated run (self-test or screenshots): throwaway data, no settings saved, no animations.</summary>
    public static bool IsRunning => OutputFile != null || Screenshots.IsRunning;

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

    // ======================================================================
    // 1.0: transfers, splits, categories, rules, rollover, months, currency, import, subscriptions,
    // net worth, templates, receipts, taxes, backup, themes
    // ======================================================================

    private static async Task NewFeatures(MainWindow window, DateTime month, DateTime lastMonth)
    {
        var today = DateTime.Today;
        var checking = DataStore.Accounts.Items.Single(a => a.Name == "Checking");
        var visa = DataStore.Accounts.Items.Single(a => a.Name == "Visa");

        // ----- Transfers -----
        decimal checkingBefore = AccountMath.Balance(checking), visaBefore = AccountMath.Balance(visa);
        var spentBefore = Budgeting.Summarize(month).Spent;
        await Step("Add a transfer", () => DataStore.Transactions.Add(new Transaction
        {
            Payee = "Card payment", Category = "Transfer", Account = "Checking", TransferAccount = "Visa", Amount = -200m, Date = today,
        }));
        Check("Transfer moves money between accounts", AccountMath.Balance(checking) == checkingBefore - 200m && AccountMath.Balance(visa) == visaBefore + 200m,
            $"{AccountMath.Balance(checking)} / {AccountMath.Balance(visa)}");
        Check("Transfer isn't spending or income", Budgeting.Summarize(month).Spent == spentBefore);

        // ----- Splits -----
        await Step("Add a split transaction", () => DataStore.Transactions.Add(new Transaction
        {
            Payee = "Costco", Category = "Split", Account = "Visa", Amount = -150m, Date = today,
            Splits = [new Split { Category = "Groceries", Amount = 100m }, new Split { Category = "Shopping", Amount = 50m }],
        }));
        var sum = Budgeting.Summarize(month);
        Check("Split counts per category", sum.SpentByCategory.GetValueOrDefault("Shopping") == 50m
            && sum.SpentByCategory.GetValueOrDefault("Groceries") >= 100m && sum.Spent == spentBefore + 150m);

        // ----- Subcategories, rename, rules -----
        await Step("Add a subcategory", () => Categories.Add("Coffee", CategoryKinds.Spending, "Dining out"));
        Check("Subcategory has its parent", Categories.Root("Coffee") == "Dining out" && Categories.Display("Coffee") == "Dining out › Coffee"
            && Categories.Spending.Contains("Coffee"));
        await Step("Coffee purchase and Dining out budget", () =>
        {
            DataStore.Transactions.Add(new Transaction { Payee = "Starbucks #123", Category = "Coffee", Account = "Visa", Amount = -10m, Date = today });
            DataStore.Budgets.Add(new Budget { Category = "Dining out", MonthlyLimit = 300m, StartMonth = month });
        });
        var dining = Budgeting.Summarize(month).Budgets.Single(b => b.Budget.Category == "Dining out");
        Check("Parent budget includes its subcategory", dining.Spent >= 285m, dining.Spent.ToString());
        Check("Reports roll subcategories into the parent", !Reports.ByCategory(month, today, DataStore.Transactions.Items).Any(c => c.Category == "Coffee"));
        await Step("Rename a category", () => Categories.Rename("Coffee", "Cafe"));
        Check("Rename updates transactions", DataStore.Transactions.Items.Any(t => t.Category == "Cafe") && !DataStore.Transactions.Items.Any(t => t.Category == "Coffee")
            && Categories.Root("Cafe") == "Dining out");
        await Step("Add a rule", () => DataStore.Rules.Add(new CategoryRule { Contains = "STARBUCKS", Category = "Cafe" }));
        Check("Rule picks the category", Categories.Suggest("Starbucks Reserve #88", income: false) == "Cafe");
        Check("History picks the category", Categories.Suggest("Fresh Thyme Market", income: false) == "Groceries");
        Check("No suggestion for an unknown payee", Categories.Suggest("Zebra Unknown", income: false) == null);

        // ----- Rollover and single-month amounts -----
        Budget? fun = null;
        await Step("Rollover budget", () =>
        {
            fun = new Budget { Category = "Entertainment", MonthlyLimit = 100m, Rollover = true, StartMonth = lastMonth };
            DataStore.Budgets.Add(fun);
            DataStore.Transactions.Add(new Transaction { Payee = "Cinema", Category = "Entertainment", Account = "Visa", Amount = -40m, Date = lastMonth.AddDays(3) });
        });
        var funStatus = Budgeting.Summarize(month).Budgets.Single(b => b.Budget.Category == "Entertainment");
        Check("Leftover rolls into this month", funStatus.Carry == 60m && funStatus.Available == 160m, $"{funStatus.Carry} / {funStatus.Available}");
        await Step("One-month amount", () => { fun!.Overrides[Budgeting.Key(month)] = 150m; DataStore.Budgets.Update(fun); });
        Check("One-month amount used", Budgeting.LimitFor(fun!, month) == 150m && Budgeting.LimitFor(fun!, lastMonth) == 100m);
        int copied = 0;
        await Step("Copy last month", () => copied = BudgetsView.CopyLastMonth(month));
        Check("Copy last month restores the usual amount", copied >= 1 && Budgeting.LimitFor(fun!, month) == 100m, copied.ToString());

        // ----- Budget month start day -----
        var prefs = DataStore.Prefs;
        prefs.MonthStartDay = 15;
        Check("Mid-month start: the 20th is in the month starting the 15th", Budgeting.MonthStart(new DateTime(2026, 11, 20)) == new DateTime(2026, 11, 15));
        Check("Mid-month start: the 3rd belongs to last month's period", Budgeting.MonthStart(new DateTime(2026, 11, 3)) == new DateTime(2026, 10, 15));
        Check("Mid-month start: Dec 14 is still in Nov 15's month", Budgeting.InMonth(new DateTime(2026, 12, 14), new DateTime(2026, 11, 15))
            && !Budgeting.InMonth(new DateTime(2026, 12, 15), new DateTime(2026, 11, 15)));
        Check("Mid-month names", Money.MonthName(new DateTime(2026, 11, 20)) == "Nov 15 – Dec 14, 2026", Money.MonthName(new DateTime(2026, 11, 20)));
        await Step("Pages with mid-month budget months", () => { MonthSwitcher.Current = Budgeting.MonthStart(today); window.GoTo("dashboard"); window.GoTo("budgets"); window.GoTo("reports"); });
        prefs.MonthStartDay = 1;
        MonthSwitcher.Current = month;

        // ----- Currency -----
        prefs.Currency = "en-GB";
        Check("Pounds", Money.Format(1284.5m) == "£1,284.50", Money.Format(1284.5m));
        prefs.Currency = "en-IE";
        Check("Euros", Money.Format(1284.5m) == "€1,284.50" && Money.TryParse("€12.50", out var eur) && eur == 12.5m, Money.Format(1284.5m));
        await Step("Pages in euros", () => { window.GoTo("dashboard"); window.GoTo("accounts"); });
        prefs.Currency = "en-US";

        // ----- Statement import -----
        Check("Amounts: (12.34)", Importer.ParseAmount("(12.34)") == -12.34m);
        Check("Amounts: 1.234,56", Importer.ParseAmount("1.234,56") == 1234.56m);
        Check("Amounts: 12.34 DR", Importer.ParseAmount("12.34 DR") == -12.34m);
        Check("Amounts: $-5.00 and -$5.00", Importer.ParseAmount("$-5.00") == -5m && Importer.ParseAmount("-$5.00") == -5m);
        Check("Dates", Importer.TryParseDate("11/05/2026", out var d1) && d1 == new DateTime(2026, 11, 5)
            && Importer.TryParseDate("2026-11-05", out var d2) && d2 == d1 && Importer.TryParseDate("25/11/2026", out var d3) && d3 == new DateTime(2026, 11, 25));

        string csv = $"Bank Export\nDate,Description,Amount,Category\n{today.ToString("MM/dd/yyyy", Money.Us)},\"STARBUCKS, STORE 9\",-4.50,\n{today.AddDays(-1).ToString("MM/dd/yyyy", Money.Us)},Paycheck ACME,2050.00,Paycheck\n{today.AddDays(-2).ToString("MM/dd/yyyy", Money.Us)},Shell Oil,-38.20,\nnot a date,Bad row,1,\n";
        var st = Importer.ReadCsv(csv);
        var map = Importer.GuessMapping(st.Headers);
        Check("CSV columns guessed", map.Date == 0 && map.Payee == 1 && map.Amount == 2 && map.Category == 3, $"{map.Date},{map.Payee},{map.Amount},{map.Category}");
        var rows = Importer.Apply(st, map, out int skipped);
        Check("CSV rows read (quoted comma kept)", rows.Count == 3 && skipped == 1 && rows[0].Payee == "STARBUCKS, STORE 9" && rows[0].Amount == -4.5m);
        Importer.MarkDuplicates(rows, "Checking");
        int added = 0;
        await Step("Import CSV", () => added = Importer.Commit(rows, "Checking"));
        Check("CSV imported with categories", added == 3 && DataStore.Transactions.Items.Any(t => t.Payee == "STARBUCKS, STORE 9" && t.Category == "Cafe")
            && DataStore.Transactions.Items.Any(t => t.Payee == "Paycheck ACME" && t.Category == "Paycheck"));
        var again = Importer.Apply(Importer.ReadCsv(csv), map, out _);
        Importer.MarkDuplicates(again, "Checking");
        Check("Re-importing finds duplicates", again.All(r => r.IsDuplicate) && Importer.Commit(again, "Checking") == 0);

        var debitCredit = Importer.ReadCsv("Posted Date;Payee;Debit;Credit\n2026-10-01;Rent;1500,00;\n2026-10-02;Refund;;25,50\n");
        var dcMap = Importer.GuessMapping(debitCredit.Headers);
        var dcRows = Importer.Apply(debitCredit, dcMap, out _);
        Check("Debit/credit columns and semicolons", dcRows.Count == 2 && dcRows[0].Amount == -1500m && dcRows[1].Amount == 25.5m,
            string.Join(", ", dcRows.Select(r => r.Amount)));

        string ofx = "OFXHEADER:100\n<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><BANKTRANLIST>"
            + "<STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20261101120000[-5:EST]<TRNAMT>-12.99<FITID>A1<NAME>NETFLIX.COM<MEMO>Streaming</STMTTRN>"
            + "<STMTTRN><TRNTYPE>CREDIT<DTPOSTED>20261102<TRNAMT>100.00<FITID>A2<NAME>Transfer in</STMTTRN>"
            + "</BANKTRANLIST><LEDGERBAL><BALAMT>1234.56<DTASOF>20261103</LEDGERBAL></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>";
        var ofxSt = Importer.ReadOfx(ofx);
        Check("OFX rows and balance", ofxSt.Rows.Count == 2 && ofxSt.Rows[0].Amount == -12.99m && ofxSt.Rows[0].ImportId == "A1"
            && ofxSt.Rows[0].Note == "Streaming" && ofxSt.LedgerBalance == 1234.56m && ofxSt.LedgerDate == new DateTime(2026, 11, 3));

        // ----- Subscriptions you might have forgotten -----
        await Step("Monthly charges", () =>
        {
            foreach (int daysAgo in new[] { 62, 31, 1 })
                DataStore.Transactions.Add(new Transaction { Payee = "Spotify P1234", Category = "Subscriptions", Account = "Visa", Amount = -10.99m, Date = today.AddDays(-daysAgo) });
        });
        var guesses = SubscriptionFinder.Find(today);
        Check("Finds the subscription", guesses.Any(g => g.Payee.StartsWith("Spotify") && g.Repeats == BillSchedule.Monthly && g.Amount == 10.99m),
            string.Join(", ", guesses.Select(g => $"{g.Payee}/{g.Repeats}")));
        Check("Bills already listed aren't suggested", !guesses.Any(g => g.Payee.Contains("Streaming", StringComparison.OrdinalIgnoreCase)));
        prefs.DismissedSubscriptions.Add("Spotify P1234");
        Check("Dismissed subscriptions stay hidden", !SubscriptionFinder.Find(today).Any(g => g.Payee.StartsWith("Spotify")));

        // ----- Net worth history, year in review, templates -----
        var history = AccountMath.NetWorthHistory(6);
        Check("Net worth history ends at today's net worth", history.Count == 6 && history[^1].Value == AccountMath.NetWorth(), $"{history[^1].Value} vs {AccountMath.NetWorth()}");
        var review = YearReview.For(today.Year);
        Check("Year in review totals", review.Spent == DataStore.Transactions.Items.Where(t => t.Date.Year == today.Year && t.IsSpending).Sum(t => -t.Amount)
            && review.TopCategories.Count > 0 && review.Months.Count == 12);
        var plan503020 = BudgetTemplateDialog.Plan(BudgetTemplateDialog.FiftyThirtyTwenty, 4000m, new Dictionary<string, decimal>());
        Check("50/30/20 template", plan503020["Savings"] == 800m && Math.Abs(plan503020.Values.Sum() - 4000m) <= 60m, plan503020.Values.Sum().ToString());
        var zero = BudgetTemplateDialog.Plan(BudgetTemplateDialog.ZeroBased, 1000m, new Dictionary<string, decimal> { ["Groceries"] = 412.3m });
        Check("Zero-based template", zero["Groceries"] == 415m && zero["Savings"] == 585m, string.Join(", ", zero.Select(kv => kv.Key + "=" + kv.Value)));

        // ----- Receipts and tax documents -----
        string tempFile = Path.Combine(TempDataFolder, "..", "coinpilot-selftest-w2.pdf");
        File.WriteAllBytes(tempFile, "%PDF-1.4 test W-2"u8.ToArray());
        string receipt = "";
        await Step("Store a receipt", () => receipt = FileVault.StoreReceipt(tempFile));
        Check("Receipt stored", File.Exists(FileVault.ReceiptPath(receipt)) && DataLock.ReadBytes(FileVault.ReceiptPath(receipt)).SequenceEqual(File.ReadAllBytes(tempFile)));
        TaxDocument? w2 = null;
        await Step("Add a tax document", () =>
        {
            w2 = new TaxDocument { Year = today.Year - 1, Title = "W-2 Acme", Kind = "W-2", OriginalName = "w2.pdf", Size = 17 };
            w2.FileName = FileVault.StoreTaxDocument(w2.Year, tempFile);
            DataStore.TaxDocuments.Add(w2);
        });
        Check("Tax document stored by year", w2 != null && File.Exists(FileVault.TaxDocumentPath(w2)) && FileVault.TaxDocumentPath(w2).Contains((today.Year - 1).ToString()));
        await Step("Move a tax document to another year", () => { FileVault.MoveTaxDocument(w2!, today.Year - 2); DataStore.TaxDocuments.Update(w2!); });
        Check("Moved with its file", File.Exists(FileVault.TaxDocumentPath(w2!)) && w2!.Year == today.Year - 2);
        TaxesView.Year = today.Year - 2;

        // ----- Every page, every color theme -----
        BillsView.ShowCalendar = true;
        await AllPages(window, "1.0");
        BillsView.ShowCalendar = false;
        foreach (var theme in ThemePalettes.All)
        {
            foreach (var mode in new[] { AppTheme.Light, AppTheme.Dark })
            {
                await Step($"Color theme {theme.Name} ({mode})", () =>
                {
                    App.Settings.ColorTheme = theme.Name;
                    App.ApplyTheme(mode);
                    window.GoTo("dashboard");
                });
            }
        }
        App.Settings.ColorTheme = ThemePalettes.Default;
        App.ApplyTheme(AppTheme.Light);
        Check("Theme layers in place", Application.Current.Resources.MergedDictionaries.Count == 3);
        Check("Color themes have readable accent", ThemePalettes.All.All(t => ThemePalettes.Swatch(t).R + ThemePalettes.Swatch(t).G + ThemePalettes.Swatch(t).B < 500));

        // ----- New dialogs -----
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            App.ApplyTheme(theme);
            await Dialog($"[{theme}] Transfer dialog", () => new TransactionDialog(DataStore.Transactions.Items.First(t => t.IsTransfer)));
            await Dialog($"[{theme}] Split dialog", () => new TransactionDialog(DataStore.Transactions.Items.First(t => t.IsSplit)));
            await Dialog($"[{theme}] Import dialog (CSV)", () => new ImportDialog("statement.csv", Importer.ReadCsv(csv)));
            await Dialog($"[{theme}] Import dialog (OFX)", () => new ImportDialog("statement.ofx", Importer.ReadOfx(ofx)));
            await Dialog($"[{theme}] Budget template dialog", () => new BudgetTemplateDialog());
            await Dialog($"[{theme}] Rollover budget dialog", () => new BudgetDialog(fun, month: month));
            await Dialog($"[{theme}] Categories dialog", () => new CategoriesDialog());
            await Dialog($"[{theme}] Tax document dialog", () => new TaxDocumentDialog(null, today.Year, tempFile, "W-2"));
            await Dialog($"[{theme}] Lock setup dialog", () => new SetLockDialog(change: false));
            await Dialog($"[{theme}] Recovery key dialog", () => new RecoveryKeyDialog("ABCDE-FGHJK-LMNPQ-RSTUV-WXYZ2") { ForceClose = true });
            await Dialog($"[{theme}] Confirm dialog", () => new ConfirmDialog("Restore?", "Replace your data?", "Restore", danger: true));
            await Dialog($"[{theme}] Linked goal dialog", () => new GoalDialog(DataStore.Goals.Items[0]));
        }
        App.ApplyTheme(AppTheme.Light);

        // ----- Linked goal -----
        await Step("Link a goal to an account", () =>
        {
            var g = DataStore.Goals.Items.Single(x => x.Name == "Emergency fund");
            g.LinkedAccount = "Checking";
            DataStore.Goals.Update(g);
            GoalMath.RefreshLinked();
        });
        Check("Linked goal shows the account balance", DataStore.Goals.Items.Single(x => x.Name == "Emergency fund").Saved == Math.Max(0, AccountMath.Balance(checking)));
        Check("Milestones", GoalMath.MilestoneCrossed(200m, 600m, 1000m) == 50 && GoalMath.MilestoneCrossed(600m, 700m, 1000m) == null && GoalMath.MilestoneCrossed(900m, 1000m, 1000m) == 100);

        // ----- Backup and restore -----
        string zip = Path.Combine(Path.GetTempPath(), $"coinpilot-selftest-{Guid.NewGuid():N}.zip");
        int countAtBackup = DataStore.Transactions.Items.Count;
        await Step("Back up", () => Backup.Create(zip));
        Check("Backup looks valid", File.Exists(zip) && Backup.Check(zip) == null);
        await Step("Change something after the backup", () => DataStore.Transactions.Add(new Transaction { Payee = "After backup", Category = "Other", Account = "Checking", Amount = -1m, Date = today }));
        string kept = "";
        await Step("Restore", () => { kept = Backup.Restore(zip); window.GoTo("dashboard"); });
        Check("Restore brings back the backed-up data", DataStore.Transactions.Items.Count == countAtBackup && !DataStore.Transactions.Items.Any(t => t.Payee == "After backup")
            && DataStore.TaxDocuments.Items.Count == 1 && File.Exists(FileVault.ReceiptPath(receipt)));
        try { File.Delete(zip); File.Delete(tempFile); Directory.Delete(kept, recursive: true); } catch { /* ignore */ }
    }

    // ======================================================================
    // PIN / password protection
    // ======================================================================

    private static async Task Security(MainWindow window)
    {
        int count = DataStore.Transactions.Items.Count;
        string recovery = "";
        await Step("Turn on protection", () => recovery = DataLock.Enable("hunter22", "Password", 15));
        string txFile = Path.Combine(DataStore.Folder, "transactions.json");
        Check("Data files are encrypted", DataLock.IsEnabled && DataLock.IsEncrypted(File.ReadAllBytes(txFile)) && recovery.Length == 29);
        var taxDoc = DataStore.TaxDocuments.Items.FirstOrDefault();
        Check("Tax documents are encrypted", taxDoc != null && DataLock.IsEncrypted(File.ReadAllBytes(FileVault.TaxDocumentPath(taxDoc))));
        Check("No plain backup copies left", !Directory.EnumerateFiles(DataStore.Folder, "*.bak", SearchOption.AllDirectories).Any());
        await Step("Save while protected", () => DataStore.Transactions.Add(new Transaction { Payee = "While locked on", Category = "Other", Account = "Checking", Amount = -2m, Date = DateTime.Today }));
        Check("New saves are encrypted", DataLock.IsEncrypted(File.ReadAllBytes(txFile)));

        await Step("Lock from the window", () => window.LockNow());
        Check("Lock screen shown and data closed", window.IsLockScreenShown && !DataLock.IsOpen && DataStore.Transactions.Items.Count == 0);
        await Step("Navigation does nothing while locked", () => window.GoTo("transactions"));
        Check("Wrong password rejected", !DataLock.Unlock("wrong-password"));
        Check("Right password accepted", DataLock.Unlock("hunter22"));
        await Step("Finish unlocking", window.CompleteUnlock);
        Check("Data back after unlocking", DataStore.Transactions.Items.Count == count + 1 && !window.IsLockScreenShown);

        DataLock.Lock();
        Check("Recovery key works", DataLock.UnlockWithRecoveryKey(recovery.ToLowerInvariant().Replace("-", " ")));
        DataStore.Reload();
        await Step("Change password to a PIN", () => DataLock.ChangeSecret("4815", "PIN"));
        DataLock.Lock();
        Check("Old password no longer works", !DataLock.Unlock("hunter22"));
        Check("New PIN works", DataLock.Unlock("4815") && DataLock.Info?.Kind == "PIN");
        DataStore.Reload();
        Check("PIN rules", DataLock.Validate("12", "PIN") != null && DataLock.Validate("12ab", "PIN") != null && DataLock.Validate("1234", "PIN") == null
            && DataLock.Validate("short", "Password") != null);
        await Step("Pages while protected", () => { window.GoTo("taxes"); window.ShowSettings(); window.GoTo("dashboard"); });
        await Step("Turn off protection", DataLock.Disable);
        Check("Files are plain again", !DataLock.IsEnabled && !DataLock.IsEncrypted(File.ReadAllBytes(txFile)) && DataStore.Transactions.Items.Count == count + 1);
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

        // ----- Reports -----
        var monthly = Reports.Monthly(month, 2, DataStore.Transactions.Items);
        Check("Reports: two months, oldest first", monthly.Count == 2 && monthly[0].Month == lastMonth && monthly[1].Month == month);
        Check("Reports: last month spent 1500, no income", monthly[0].Spent == 1500m && monthly[0].Income == 0 && monthly[0].SavingsRate == null);
        Check("Reports: this month totals", monthly[1].Income == 2050m && monthly[1].Spent == 522.52m && monthly[1].Net == 1527.48m);
        var byCat = Reports.ByCategory(month, month.AddMonths(1).AddDays(-1), DataStore.Transactions.Items);
        Check("Reports: Dining out is the top category", byCat.Count == 3 && byCat[0].Category == "Dining out" && byCat[0].Spent == 275m,
            string.Join(", ", byCat.Select(c => c.Category + "=" + c.Spent)));

        // ----- Accounts -----
        await Step("Add accounts", () =>
        {
            DataStore.Accounts.Add(new Account { Name = "Checking", Type = AccountTypes.Checking, StartingBalance = 1000m, BalanceDate = month.AddDays(-1) });
            DataStore.Accounts.Add(new Account { Name = "Credit card", Type = AccountTypes.CreditCard, StartingBalance = -500m, BalanceDate = month.AddDays(-1) });
        });
        var checking = DataStore.Accounts.Items.Single(a => a.Name == "Checking");
        var card = DataStore.Accounts.Items.Single(a => a.Name == "Credit card");
        Check("Checking balance = start + later transactions (older ones ignored)", AccountMath.Balance(checking) == 2963.58m, AccountMath.Balance(checking).ToString());
        Check("Credit card owes 936.10", AccountMath.Balance(card) == -936.10m && card.IsLiability, AccountMath.Balance(card).ToString());
        Check("Net worth = 2963.58 - 936.10", AccountMath.NetWorth() == 2027.48m, AccountMath.NetWorth().ToString());
        Check("Transaction account choices come from Accounts", DataStore.AccountChoices().SequenceEqual(new[] { "Checking", "Credit card" }));
        await Step("Rename an account", () =>
        {
            card.Name = "Visa";
            DataStore.Accounts.Update(card);
            AccountMath.Rename("Credit card", "Visa");
        });
        Check("Rename moves its transactions", DataStore.Transactions.Items.Count(t => t.Account == "Visa") == 3
                                                && !DataStore.Transactions.Items.Any(t => t.Account == "Credit card"));
        Check("Balance follows the rename", AccountMath.Balance(card) == -936.10m);

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
            await Dialog($"[{theme}] Add account dialog", () => new AccountDialog(null, "Cash"));
            await Dialog($"[{theme}] Edit account dialog", () => new AccountDialog(DataStore.Accounts.Items[0]));
            await Dialog($"[{theme}] Add debt dialog", () => new DebtDialog(null));
            await Dialog($"[{theme}] Add bill dialog", () => new BillDialog(null));
            await Dialog($"[{theme}] Add goal dialog", () => new GoalDialog(null));
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

        // ----- Debts: payoff plans -----
        var single = new Debt { Id = "a", Name = "Zero-rate", Balance = 1200m, InterestRate = 0, MinimumPayment = 100m };
        var p1 = DebtPlanner.Plan([single], DebtPlanner.Avalanche, 0);
        Check("Plan: $1,200 at 0% paying $100 takes 12 months", p1.Months == 12 && p1.TotalInterest == 0 && !p1.NeverPaysOff, p1.Months.ToString());
        var p1x = DebtPlanner.Plan([single], DebtPlanner.Avalanche, 100m);
        Check("Plan: an extra $100 halves it", p1x.Months == 6, p1x.Months.ToString());
        var low = new Debt { Id = "low", Name = "Low", Balance = 1000m, InterestRate = 0, MinimumPayment = 100m };
        var high = new Debt { Id = "high", Name = "High", Balance = 2000m, InterestRate = 24m, MinimumPayment = 100m };
        var aval = DebtPlanner.Plan([low, high], DebtPlanner.Avalanche, 50m);
        var snow = DebtPlanner.Plan([low, high], DebtPlanner.Snowball, 50m);
        Check("Avalanche pays the highest rate first", aval.Order["high"] == 1 && aval.Order["low"] == 2);
        Check("Snowball pays the smallest balance first", snow.Order["low"] == 1 && snow.Order["high"] == 2);
        Check("Avalanche costs less interest than snowball", aval.TotalInterest < snow.TotalInterest && !aval.NeverPaysOff && !snow.NeverPaysOff,
            $"{aval.TotalInterest} vs {snow.TotalInterest}");
        var stuck = new Debt { Id = "s", Name = "Stuck", Balance = 10000m, InterestRate = 24m, MinimumPayment = 50m };
        Check("Plan: payments below the interest never pay off", DebtPlanner.Plan([stuck], DebtPlanner.Avalanche, 0).NeverPaysOff);

        int txBefore = DataStore.Transactions.Items.Count;
        await Step("Add a debt and record a payment", () =>
        {
            var visa = new Debt { Name = "Visa card", Balance = 3000m, OriginalBalance = 3000m, InterestRate = 22.99m, MinimumPayment = 90m };
            DataStore.Debts.Add(visa);
            DataStore.Debts.Add(new Debt { Name = "Car loan", Type = "Car loan", Balance = 8000m, OriginalBalance = 9000m, InterestRate = 6.5m, MinimumPayment = 250m });
            DebtPaymentDialog.Apply(visa, 500m, DateTime.Today, "Checking", addTransaction: true);
        });
        var visaDebt = DataStore.Debts.Items.Single(d => d.Name == "Visa card");
        Check("Payment lowers the balance", visaDebt.Balance == 2500m && visaDebt.PaidOff == 500m && visaDebt.Payments.Count == 1);
        Check("Payment added to Transactions as Debt payments", DataStore.Transactions.Items.Count == txBefore + 1
            && DataStore.Transactions.Items.Any(t => t.Category == Categories.DebtPayments && t.Amount == -500m));
        Check("Net worth subtracts debts", AccountMath.NetWorth() == AccountMath.Balance(checking) + AccountMath.Balance(card) - 10500m);
        await Step("Debt plan settings save", () => { DataStore.DebtPlan.Strategy = DebtPlanner.Snowball; DataStore.DebtPlan.ExtraPayment = 75m; DataStore.DebtPlan.Save(); });
        var reloadedPlan = DebtPlanSettings.Load(DataStore.Folder);
        Check("Debt plan settings reload", reloadedPlan.Strategy == DebtPlanner.Snowball && reloadedPlan.ExtraPayment == 75m);

        // ----- Bills -----
        Check("Monthly bill on the 31st keeps its day", BillSchedule.Next(new DateTime(2027, 1, 31), BillSchedule.Monthly, 31) == new DateTime(2027, 2, 28)
            && BillSchedule.Next(new DateTime(2027, 2, 28), BillSchedule.Monthly, 31) == new DateTime(2027, 3, 31));
        Check("Weekly $12 costs $52 a month", BillSchedule.MonthlyCost(new Bill { Amount = 12m, Repeats = BillSchedule.Weekly }) == 52m);
        Check("Yearly $120 costs $10 a month", BillSchedule.MonthlyCost(new Bill { Amount = 120m, Repeats = BillSchedule.Yearly }) == 10m);
        var today = DateTime.Today;
        Check("Yesterday's bill is overdue", BillSchedule.Status(new Bill { NextDue = today.AddDays(-1) }, today) == BillStatus.Overdue);
        Check("Bill in 3 days is due soon", BillSchedule.Status(new Bill { NextDue = today.AddDays(3) }, today) == BillStatus.DueSoon);
        Check("Bill in 20 days is upcoming", BillSchedule.Status(new Bill { NextDue = today.AddDays(20) }, today) == BillStatus.Upcoming);
        Bill? rent = null, once = null;
        await Step("Add bills and mark them paid", () =>
        {
            rent = new Bill { Name = "Rent", Amount = 1500m, NextDue = today, DueDay = today.Day, Repeats = BillSchedule.Monthly, Category = "Housing", Account = "Checking" };
            once = new Bill { Name = "Car registration", Amount = 85m, NextDue = today.AddDays(2), Repeats = BillSchedule.Once, Category = "Transportation", Account = "Checking" };
            DataStore.Bills.Add(rent);
            DataStore.Bills.Add(once);
            DataStore.Bills.Add(new Bill { Name = "Streaming", Amount = 15.49m, NextDue = today.AddDays(-2), Repeats = BillSchedule.Monthly, Category = "Subscriptions", Account = "Visa" });
            BillSchedule.MarkPaid(rent, today);
            BillSchedule.MarkPaid(once, today);
        });
        Check("Paid monthly bill moves to next month", rent != null && rent.NextDue == BillSchedule.Next(today, BillSchedule.Monthly, today.Day) && rent.LastPaid == today);
        Check("Paid one-time bill is done", once != null && once.IsDone && BillSchedule.Status(once, today) == BillStatus.Paid);
        Check("Mark paid adds transactions", DataStore.Transactions.Items.Any(t => t.Payee == "Rent" && t.Amount == -1500m && t.Category == "Housing")
            && DataStore.Transactions.Items.Any(t => t.Payee == "Car registration" && t.Amount == -85m));

        // ----- Goals -----
        Goal? fund = null;
        await Step("Add a goal and money to it", () =>
        {
            fund = new Goal { Name = "Emergency fund", Target = 1200m, TargetDate = today.AddMonths(12) };
            DataStore.Goals.Add(fund);
            DataStore.Goals.Add(new Goal { Name = "Trip", Target = 2000m, Saved = 2000m });
            GoalMath.AddMoney(fund, 300m, today);
        });
        Check("Goal saved and history kept", fund != null && fund.Saved == 300m && fund.History.Count == 1);
        Check("Goal: $900 left over 12 months = $75/month", fund != null && GoalMath.MonthlyNeeded(fund, today) == 75m,
            fund == null ? "" : GoalMath.MonthlyNeeded(fund, today)?.ToString() ?? "null");
        Check("Reached goal has no monthly amount", DataStore.Goals.Items.Single(g => g.Name == "Trip") is { IsReached: true } trip && GoalMath.MonthlyNeeded(trip, today) == null);
        await Step("Take out more than saved", () => GoalMath.AddMoney(fund!, -500m, today));
        Check("Goal can't go below zero", fund!.Saved == 0m);

        var reloadedGoals = new JsonStore<Goal>(DataStore.Folder, "goals");
        var reloadedBills = new JsonStore<Bill>(DataStore.Folder, "bills");
        var reloadedDebts = new JsonStore<Debt>(DataStore.Folder, "debts");
        Check("Goals, bills and debts saved to disk", reloadedGoals.Items.Count == 2 && reloadedBills.Items.Count == 3 && reloadedDebts.Items.Count == 2
            && reloadedDebts.Items.Single(d => d.Name == "Visa card").Payments.Count == 1 && reloadedBills.Items.Single(b => b.Name == "Car registration").IsDone);

        // ----- Every page with everything filled in -----
        await AllPages(window, "full");
        foreach (var range in ReportsView.Ranges)
        {
            await Step($"Reports: {range}", () => { ReportsView.Range = range; window.GoTo("reports"); });
        }
        ReportsView.Range = ReportsView.SixMonths;
        await Step("Debts page with snowball plan and paid-off debt", () =>
        {
            var car = DataStore.Debts.Items.Single(d => d.Name == "Car loan");
            DebtPaymentDialog.Apply(car, 8000m, DateTime.Today, "Checking", addTransaction: false);
            window.GoTo("debts");
        });
        Check("Paying the full balance marks the debt paid off", DataStore.Debts.Items.Single(d => d.Name == "Car loan").IsPaidOff);

        await NewFeatures(window, month, lastMonth);

        // ----- Catalog / navigation -----
        string[] working = ["dashboard", "transactions", "budgets", "accounts", "debts", "bills", "goals", "reports", "taxes"];
        Check("Every section works except Sync & Devices (coming soon)",
            ModuleCatalog.Sidebar.All(m => m.IsComingSoon == !working.Contains(m.Key)));
        Check("Every coming-soon section lists planned features",
            ModuleCatalog.Sidebar.Where(m => m.IsComingSoon).All(m => m.Features.Length > 0 && m.Description.Length > 0));
        Check("Section keys are unique",
            ModuleCatalog.Sidebar.Select(m => m.Key).Distinct().Count() == ModuleCatalog.Sidebar.Count);
        Check("Version is set", App.VersionText != "0.0.0", App.VersionText);
        await Step("Toast", () => window.ShowToast("Self-test toast"));
        await Step("Unknown page falls back to the Dashboard", () => window.GoTo("no-such-page"));
        Check("Dashboard selected after unknown page", ModuleCatalog.Dashboard.IsSelected);

        await Security(window);

        Report.AppendLine(_failed == 0 ? "ALL PASSED" : $"{_failed} FAILED");
        try { File.WriteAllText(OutputFile!, Report.ToString()); } catch { /* ignore */ }
        try { Directory.Delete(TempDataFolder, recursive: true); } catch { /* ignore */ }
        return _failed;
    }
}
