using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CoinPilot.Data;
using CoinPilot.Models;
using CoinPilot.Views;

namespace CoinPilot.Services;

/// <summary>
/// Headless checks run by CI: <c>WillowWallet.exe --self-test result.txt</c>.
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

        // Payment apps' own downloads
        var paypal = Importer.ReadCsv("\"Date\",\"Time\",\"TimeZone\",\"Name\",\"Type\",\"Status\",\"Currency\",\"Gross\",\"Fee\",\"Net\",\"Transaction ID\"\n" +
            "\"11/02/2026\",\"10:15:00\",\"EST\",\"Etsy Inc\",\"Express Checkout Payment\",\"Completed\",\"USD\",\"-24.99\",\"0.00\",\"-24.99\",\"1AB\"\n" +
            "\"11/03/2026\",\"09:00:00\",\"EST\",\"Jamie Lee\",\"Mobile Payment\",\"Completed\",\"USD\",\"60.00\",\"-2.04\",\"57.96\",\"2CD\"\n" +
            "\"11/04/2026\",\"09:00:00\",\"EST\",\"Old Navy\",\"Express Checkout Payment\",\"Denied\",\"USD\",\"-40.00\",\"0.00\",\"-40.00\",\"3EF\"\n");
        var ppMap = Importer.GuessMapping(paypal.Headers);
        var ppRows = Importer.Apply(paypal, ppMap, out int ppSkipped);
        Check("PayPal download: net amounts, denied skipped", ppMap.Source == "PayPal" && ppRows.Count == 2 && ppSkipped == 1
            && ppRows[0] is { Payee: "Etsy Inc", Amount: -24.99m } && ppRows[1] is { Payee: "Jamie Lee", Amount: 57.96m },
            string.Join(", ", ppRows.Select(r => $"{r.Payee} {r.Amount}")));
        var venmo = Importer.ReadCsv("Account Statement - (@alex-g) ,,,,,,,,,\nAccount Activity,,,,,,,,,\n" +
            ",ID,Datetime,Type,Status,Note,From,To,Amount (total),Funding Source\n" +
            ",,,,,,,,,\n" +
            ",3912,2026-11-05T19:02:11,Payment,Complete,Pizza 🍕,Alex Glassner,Sam Ortiz,- $18.50,Venmo balance\n" +
            ",3913,2026-11-06T08:30:00,Payment,Complete,Gas money,Chris Park,Alex Glassner,+ $25.00,\n");
        var vmMap = Importer.GuessMapping(venmo.Headers);
        var vmRows = Importer.Apply(venmo, vmMap, out _);
        Check("Venmo download: who you paid or who paid you", vmMap.Source == "Venmo" && vmRows.Count == 2
            && vmRows[0] is { Payee: "Sam Ortiz", Amount: -18.5m, Note: "Pizza 🍕" } && vmRows[1] is { Payee: "Chris Park", Amount: 25m } && vmRows[0].Date == new DateTime(2026, 11, 5),
            string.Join(", ", vmRows.Select(r => $"{r.Payee} {r.Amount} {r.Date:d}")));
        var cashApp = Importer.ReadCsv("Transaction ID,Date,Transaction Type,Currency,Amount,Fee,Net Amount,Asset Type,Asset Price,Asset Amount,Status,Notes,Name of sender/receiver,Account\n" +
            "abc1,2026-11-07 14:22:05 EST,Cash Card Debit,USD,-$12.40,$0,-$12.40,,,,CARD CHARGED,,Chipotle,Visa Debit 1234\n" +
            "abc2,2026-11-08 09:10:00 EST,P2P,USD,$40,$0,$40,,,,PAYMENT SENT,Rent share,Morgan,Cash Balance\n" +
            "abc3,2026-11-08 10:00:00 EST,P2P,USD,-$15,$0,-$15,,,,FAILED,,Taylor,Cash Balance\n");
        var caMap = Importer.GuessMapping(cashApp.Headers);
        var caRows = Importer.Apply(cashApp, caMap, out int caSkipped);
        Check("Cash App download: dates with time zones, failed skipped", caMap.Source == "Cash App" && caRows.Count == 2 && caSkipped == 1
            && caRows[0] is { Payee: "Chipotle", Amount: -12.4m } && caRows[0].Date == new DateTime(2026, 11, 7) && caRows[1] is { Payee: "Morgan", Amount: 40m, Note: "Rent share" },
            string.Join(", ", caRows.Select(r => $"{r.Payee} {r.Amount} {r.Date:d}")));
        Check("Payment app account type", AccountTypes.All.Contains(AccountTypes.PaymentApp) && AccountTypes.PaymentApps.Select(p => p.Name).SequenceEqual(["PayPal", "Venmo", "Cash App", "Apple Cash"]));

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

        // ----- Date picker popup uses the app's calendar design -----
        {
            var picker = new System.Windows.Controls.DatePicker { SelectedDate = today };
            var probe = new Window { Content = picker, Width = 400, Height = 200, Left = -2000, Top = -2000, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            probe.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            picker.IsDropDownOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var popup = picker.Template.FindName("PART_Popup", picker) as System.Windows.Controls.Primitives.Popup;
            var cal = popup?.Child as System.Windows.Controls.Calendar;
            Check("Date picker popup is themed", cal != null && cal.Style == Application.Current.FindResource("ModernCalendar"),
                cal == null ? "no calendar in the popup" : cal.Style == null ? "no style" : "a different style");
            picker.IsDropDownOpen = false;
            probe.Close();
        }

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
            await Dialog($"[{theme}] What's new", () => new WhatsNewDialog());
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

        // A backup made on the phone (WillowWallet for Android writes the same files, with UTC timestamps and an Id in the settings)
        string phoneZip = Path.Combine(Path.GetTempPath(), $"ww-phone-backup-{Guid.NewGuid():N}.zip");
        using (var z = System.IO.Compression.ZipFile.Open(phoneZip, System.IO.Compression.ZipArchiveMode.Create))
        {
            void Entry(string name, string text)
            {
                using var w = new StreamWriter(z.CreateEntry(name).Open());
                w.Write(text);
            }
            Entry("transactions.json", "[{\"Id\":\"ph1\",\"Payee\":\"Corner Cafe\",\"Category\":\"Dining out\",\"Account\":\"Checking\",\"Date\":\"2026-11-10T00:00:00\",\"Amount\":-12.5,\"Note\":\"\",\"CreatedAt\":\"2026-11-10T14:00:00.000Z\",\"UpdatedAt\":\"2026-11-10T14:00:00.000Z\",\"Splits\":[],\"Tags\":[\"phone\"],\"Receipt\":\"\",\"TransferAccount\":\"\"}]");
            Entry("budgets.json", "[{\"Rollover\":true,\"Id\":\"phb\",\"Category\":\"Dining out\",\"MonthlyLimit\":150.0,\"CreatedAt\":\"2026-11-01T00:00:00.000Z\",\"UpdatedAt\":\"2026-11-01T00:00:00.000Z\"}]");
            Entry("bills.json", "[{\"Id\":\"phbill\",\"CreatedAt\":\"2026-11-01T00:00:00.000Z\",\"UpdatedAt\":\"2026-11-01T00:00:00.000Z\",\"Name\":\"Phone\",\"Amount\":65.0,\"NextDue\":\"2026-12-12T00:00:00\",\"DueDay\":12,\"Repeats\":\"Monthly\",\"Category\":\"Utilities\",\"Account\":\"Checking\",\"AutoPay\":false,\"LastPaid\":null,\"Note\":\"\",\"DebtId\":\"\"}]");
            Entry("accounts.json", "[{\"Id\":\"pha\",\"CreatedAt\":\"2026-11-01T00:00:00.000Z\",\"UpdatedAt\":\"2026-11-01T00:00:00.000Z\",\"Name\":\"Checking\",\"Type\":\"Checking\",\"Institution\":\"\",\"StartingBalance\":1000.0,\"BalanceDate\":\"2026-11-01T00:00:00\",\"IsClosed\":false}]");
            Entry("preferences.json", "{\"Currency\":\"en-US\",\"MonthStartDay\":1,\"SkippedCuts\":[\"hulu|forever\"],\"SetupDone\":true,\"UpdatedAt\":\"2026-11-10T14:00:00.000Z\"}");
            Entry("debt-plan.json", "{\"Strategy\":\"Snowball\",\"ExtraPayment\":50.0,\"UpdatedAt\":\"2026-11-10T14:00:00.000Z\"}");
        }
        Check("A phone backup looks valid", Backup.Check(phoneZip) == null);
        string keptPhone = "";
        await Step("Restore a backup made on the phone", () => { keptPhone = Backup.Restore(phoneZip); window.GoTo("dashboard"); window.GoTo("budgets"); });
        Check("Phone backup restored", DataStore.Transactions.Items.SingleOrDefault()?.Amount == -12.5m && DataStore.Transactions.Items.Single().Tags.SequenceEqual(["phone"])
            && DataStore.Budgets.Items.Single().Rollover && DataStore.Bills.Items.Single().Amount == 65m && DataStore.Accounts.Items.Single().StartingBalance == 1000m
            && DataStore.Prefs.SkippedCuts.Contains("hulu|forever") && DataStore.DebtPlan.Strategy == DebtPlanner.Snowball && DataStore.DebtPlan.ExtraPayment == 50m,
            $"{DataStore.Transactions.Items.Count} transactions, {DataStore.Budgets.Items.Count} budgets");
        // Put the data from before back for the tests that follow
        await Step("Restore the data from before", () =>
        {
            string folder = DataStore.Folder;
            FileVault.CleanUpOpenedFiles();
            Directory.Delete(folder, recursive: true);
            Directory.Move(keptPhone, folder);
            DataLock.Forget();
            DataStore.Reload();
            window.GoTo("dashboard");
        });
        Check("Data from before is back", DataStore.Transactions.Items.Count == countAtBackup);
        try { File.Delete(phoneZip); } catch { /* ignore */ }
    }

    // ======================================================================
    // PIN / password protection
    // ======================================================================

    // ----- Sync with a real WillowWallet Server (CI starts one and sets WW_TEST_SERVER) -----

    private static async Task Sync(MainWindow window)
    {
        await Step("Sync page before connecting", () => window.GoTo("sync"));
        Check("Sync is off until you connect", SyncEngine.Status == SyncEngine.State.Off && !SyncEngine.IsConnected);
        string? url = Environment.GetEnvironmentVariable("WW_TEST_SERVER");
        string? adminPassword = Environment.GetEnvironmentVariable("WW_TEST_ADMIN_PASSWORD");
        if (url == null || adminPassword == null)
        {
            Report.AppendLine("SKIP  Sync with a real server (WW_TEST_SERVER not set)");
            return;
        }
        const string user = "selftest", password = "self-test-password";
        var phoneDevice = new WillowWallet.Sync.DeviceInfo("Test phone", "Android", "0.2.0");
        string? recovery = null;
        try
        {
            string invite = await TestServerAdmin.Invite(url, adminPassword);
            int localTx = DataStore.Transactions.Items.Count;

            // Create the account from this PC and upload everything
            (_, recovery) = await SyncEngine.ConnectAsync(url, user, password, create: true, invite, iterations: 100_000);
            Check("Account created, recovery key shown", recovery != null && WillowWallet.Sync.SyncCrypto.ParseRecoveryKey(recovery) != null, recovery ?? "");
            Check("First sync", await SyncEngine.FirstSyncAsync(useServerOnly: false), SyncEngine.Problem ?? "");
            Check("Nothing left to send", SyncEngine.PendingCount == 0, SyncEngine.PendingCount.ToString());
            await Step("Sync page when connected", () => window.GoTo("sync"));

            // Another device sees it all, encrypted on the server
            using var phone = new WillowWallet.Sync.SyncApiClient(url);
            var signed = await WillowWallet.Sync.SyncAccount.LoginAsync(phone, user, password, phoneDevice);
            var server = await PullAll(phone, signed.DataKey);
            Check("Server has every transaction", server.Count(kv => kv.Key.StartsWith("transactions/")) == localTx, $"{server.Count(kv => kv.Key.StartsWith("transactions/"))} of {localTx}");
            Check("Server has budgets, accounts, bills, goals, debts", new[] { "budgets", "accounts", "bills", "goals", "debts" }.All(c => server.Keys.Any(k => k.StartsWith(c + "/"))));
            Check("Settings sync too", server.ContainsKey("settings/preferences") || DataStore.Prefs.UpdatedAt == default);
            var taxWithFile = DataStore.TaxDocuments.Items.FirstOrDefault(d => d.FileName.Length > 0 && File.Exists(FileVault.TaxDocumentPath(d)));
            if (taxWithFile != null)
            {
                var sealedFile = await phone.DownloadFileAsync("tax/" + taxWithFile.FileName);
                Check("Tax document uploaded (encrypted)", sealedFile != null &&
                    WillowWallet.Sync.SyncCrypto.OpenBytes(signed.DataKey, sealedFile, WillowWallet.Sync.SyncCrypto.FileAad("tax/" + taxWithFile.FileName)).SequenceEqual(DataLock.ReadBytes(FileVault.TaxDocumentPath(taxWithFile))));
            }

            // Changes from the phone: a new transaction, an edit and a delete
            var items = DataStore.Transactions.Items.OrderBy(t => t.Date).ToList();
            var edited = items[0];
            var deleted = items[1];
            var added = new Transaction { Id = "phone-" + Guid.NewGuid().ToString("N")[..8], Payee = "From the phone", Category = "Groceries", Account = "Checking", Date = DateTime.Today, Amount = -12.34m, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now.AddSeconds(5) };
            var editedCopy = System.Text.Json.JsonSerializer.Deserialize<Transaction>(JsonStore<Transaction>.ToJson(edited))!;
            editedCopy.Payee = "Edited on the phone";
            editedCopy.UpdatedAt = DateTime.Now.AddSeconds(5);
            await phone.PushAsync(new WillowWallet.Sync.PushRequest([
                Remote(signed.DataKey, "transactions", added),
                Remote(signed.DataKey, "transactions", editedCopy),
                new WillowWallet.Sync.PushChange("transactions", deleted.Id, DateTimeOffset.UtcNow.AddSeconds(5).ToUnixTimeMilliseconds(), true, null),
            ]));
            Check("Sync now", await SyncEngine.SyncNowAsync(manual: true), SyncEngine.Problem ?? "");
            Check("Phone's new transaction arrived", DataStore.Transactions.Find(added.Id)?.Payee == "From the phone");
            Check("Phone's edit arrived", DataStore.Transactions.Find(edited.Id)?.Payee == "Edited on the phone");
            Check("Phone's delete arrived", DataStore.Transactions.Find(deleted.Id) == null);
            Check("Applying the phone's changes didn't queue them to send back", SyncEngine.PendingCount == 0, SyncEngine.PendingCount.ToString());

            // A change here reaches the phone
            var mine = DataStore.Transactions.Items.First(t => t.Id != added.Id);
            await Step("Edit a transaction here", () => { mine.Note = "Edited on the PC"; DataStore.Transactions.Update(mine); });
            Check("It's waiting to sync", SyncEngine.PendingCount == 1, SyncEngine.PendingCount.ToString());
            await SyncEngine.SyncNowAsync(manual: true);
            server = await PullAll(phone, signed.DataKey);
            Check("Phone sees the PC's edit", server["transactions/" + mine.Id].Contains("Edited on the PC"));

            // Same record changed in both places: the newer change wins either way
            var both = DataStore.Transactions.Items.First(t => t.Id != added.Id && t.Id != mine.Id);
            var newerRemote = System.Text.Json.JsonSerializer.Deserialize<Transaction>(JsonStore<Transaction>.ToJson(both))!;
            newerRemote.Payee = "Phone wins";
            newerRemote.UpdatedAt = DateTime.Now.AddMinutes(2);
            await Step("Edit the same transaction here (older)", () => { both.Payee = "PC loses"; DataStore.Transactions.Update(both); });
            await phone.PushAsync(new WillowWallet.Sync.PushRequest([Remote(signed.DataKey, "transactions", newerRemote)]));
            await SyncEngine.SyncNowAsync(manual: true);
            Check("Newer phone edit wins", DataStore.Transactions.Find(both.Id)?.Payee == "Phone wins", DataStore.Transactions.Find(both.Id)?.Payee ?? "missing");
            var olderRemote = System.Text.Json.JsonSerializer.Deserialize<Transaction>(JsonStore<Transaction>.ToJson(mine))!;
            olderRemote.Payee = "Old phone edit";
            olderRemote.UpdatedAt = DateTime.Now.AddHours(-3);
            await phone.PushAsync(new WillowWallet.Sync.PushRequest([Remote(signed.DataKey, "transactions", olderRemote)]));
            await Step("Edit here (newer)", () => { mine.Payee = "PC wins"; DataStore.Transactions.Update(mine); });
            await SyncEngine.SyncNowAsync(manual: true);
            Check("Newer PC edit wins", DataStore.Transactions.Find(mine.Id)?.Payee == "PC wins");
            server = await PullAll(phone, signed.DataKey);
            Check("Phone gets the PC's newer edit", server["transactions/" + mine.Id].Contains("PC wins"));

            // Deleting here
            var gone = DataStore.Transactions.Items.Last();
            await Step("Delete a transaction here", () => DataStore.Transactions.Delete(gone));
            await SyncEngine.SyncNowAsync(manual: true);
            server = await PullAll(phone, signed.DataKey);
            Check("Phone sees the delete", !server.ContainsKey("transactions/" + gone.Id));

            // Stopped on the server: changes wait here
            await TestServerAdmin.Post(url, adminPassword, "/admin/api/service/stop");
            await Step("Edit while sync is stopped", () => { mine.Note = "While stopped"; DataStore.Transactions.Update(mine); });
            Check("Sync reports it's stopped", !await SyncEngine.SyncNowAsync() && SyncEngine.Status == SyncEngine.State.Paused, SyncEngine.Status.ToString());
            await TestServerAdmin.Post(url, adminPassword, "/admin/api/service/start");
            Check("Catches up when started again", await SyncEngine.SyncNowAsync(manual: true) && SyncEngine.PendingCount == 0);

            // Stop syncing: data stays here
            int before = DataStore.Transactions.Items.Count;
            await SyncEngine.SignOutAsync();
            Check("Stop syncing keeps the data here", SyncEngine.Status == SyncEngine.State.Off && DataStore.Transactions.Items.Count == before);

            // Sign in again using only the server's copy
            using (DataStore.Batch()) DataStore.Transactions.Add(new Transaction { Payee = "Only on this PC", Amount = -1, Category = "Other", Account = "Cash" });
            await SyncEngine.ConnectAsync(url, user, password, create: false, null);
            Check("Sign in and use only the server's data", await SyncEngine.FirstSyncAsync(useServerOnly: true));
            Check("Local-only record replaced by the server's copy", DataStore.Transactions.Items.All(t => t.Payee != "Only on this PC") && DataStore.Transactions.Items.Count == before);

            // Forgot the password
            await SyncEngine.SignOutAsync();
            await SyncEngine.RecoverAsync(url, user, recovery!, "a-new-password-1", iterations: 100_000);
            Check("Recovery key sets a new password", SyncEngine.IsConnected && await SyncEngine.SyncNowAsync(manual: true));
            var wrong = await Catch(() => SyncEngine.ConnectAsync(url, user, password, create: false, null));
            Check("Old password no longer works", wrong?.Error == "bad_login", wrong?.Error ?? "no error");
            await SyncEngine.SignOutAsync();
        }
        catch (Exception ex)
        {
            Check("Sync with a real server", false, ex.ToString());
            try { await SyncEngine.SignOutAsync(); } catch { }
        }
    }

    private static WillowWallet.Sync.PushChange Remote(byte[] key, string collection, Transaction t) => new(collection, t.Id,
        new DateTimeOffset(t.UpdatedAt).ToUnixTimeMilliseconds(), false, WillowWallet.Sync.SyncAccount.SealRecord(key, collection, t.Id, JsonStore<Transaction>.ToJson(t)));

    /// <summary>Everything on the server for this account, decrypted: "collection/id" → JSON.</summary>
    private static async Task<Dictionary<string, string>> PullAll(WillowWallet.Sync.SyncApiClient api, byte[] key)
    {
        var all = new Dictionary<string, string>();
        long since = 0;
        while (true)
        {
            var page = await api.ChangesAsync(since);
            foreach (var c in page.Changes)
            {
                if (c.Deleted) all.Remove(c.Collection + "/" + c.Id);
                else all[c.Collection + "/" + c.Id] = WillowWallet.Sync.SyncAccount.OpenRecord(key, c.Collection, c.Id, c.Payload!);
            }
            since = page.Changes.Count > 0 ? page.Changes[^1].Seq : page.Latest;
            if (!page.More) return all;
        }
    }

    private static async Task<WillowWallet.Sync.SyncApiException?> Catch(Func<Task> action)
    {
        try { await action(); return null; }
        catch (WillowWallet.Sync.SyncApiException e) { return e; }
    }

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
        var keyCopy = DataLock.CopyKey();
        Check("Windows Hello: the right key opens the data, a wrong one doesn't", keyCopy != null && !DataLock.UnlockWithKey(new byte[32]) && DataLock.UnlockWithKey(keyCopy!));
        Check("Windows Hello is off until turned on", !WindowsHello.IsOn);
        var releases = WhatsNewDialog.Releases(WhatsNewDialog.ReadChangelog());
        Check("What's new lists this version first", releases.Count > 5 && releases[0].Version.StartsWith(App.VersionText) && releases[0].Lines.Count > 0, releases.FirstOrDefault().Version ?? "(none)");
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

    /// <summary>A control in the window found by its accessible name (as a screen reader would).</summary>
    private static T? Named<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T t && System.Windows.Automation.AutomationProperties.GetName(t) == name) return t;
            if (Named<T>(child, name) is { } found) return found;
        }
        return null;
    }

    /// <summary>Get started from a blank WillowWallet: typed in like a person would, then Delete all data.</summary>
    private static async Task GetStartedFlow(MainWindow window)
    {
        Check("Starts with no data", DataStore.IsEmpty);
        await Step("Open Get started", () => window.ShowGetStarted());
        var gs = window.Setup;
        Check("Get started opens on Welcome", gs?.CurrentStep == "Welcome", gs?.CurrentStep ?? "not shown");
        if (gs == null) return;

        await Step("Welcome → accounts", () => gs.Advance());
        await Step("Type a checking account and a credit card", () =>
        {
            Named<System.Windows.Controls.TextBox>(gs, "Account name")!.Text = "Everyday checking";
            Named<System.Windows.Controls.TextBox>(gs, "Bank")!.Text = "Chase";
            Named<System.Windows.Controls.TextBox>(gs, "Balance")!.Text = "1,250.00";
            Named<System.Windows.Controls.Button>(gs, "Add account")!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Named<System.Windows.Controls.RadioButton>(gs, AccountTypes.CreditCard)!.IsChecked = true;
            Named<System.Windows.Controls.TextBox>(gs, "Balance")!.Text = "430";
        });
        await Step("Next adds the card left in the form", () => gs.Advance());
        var checking = DataStore.Accounts.Items.FirstOrDefault(a => a.Name == "Everyday checking");
        var card = DataStore.Accounts.Items.FirstOrDefault(a => a.Type == AccountTypes.CreditCard);
        Check("Accounts added with today's balances", checking is { StartingBalance: 1250m, Institution: "Chase" } && card is { Name: "Credit card", StartingBalance: -430m },
            string.Join(", ", DataStore.Accounts.Items.Select(a => $"{a.Name} {a.StartingBalance}")));
        Check("On the income step", gs.CurrentStep == "Income", gs.CurrentStep);

        await Step("Paycheck 4,000 a month with 50/30/20", () => { gs.SetPaycheck("4000", IncomeSchedule.Monthly, BudgetTemplateDialog.FiftyThirtyTwenty); gs.Advance(); });
        var pay = DataStore.Income.Items.FirstOrDefault();
        Check("Paycheck saved as repeating income", DataStore.Income.Count == 1 && pay is { Name: "Paycheck", Amount: 4000m, Repeats: IncomeSchedule.Monthly, Varies: false, AutoAdd: true } && pay.NextDate >= DateTime.Today,
            pay == null ? "none" : $"{pay.Name} {pay.Amount} {pay.Repeats} {pay.NextDate:d}");
        decimal budgeted = DataStore.Budgets.Items.Sum(b => b.MonthlyLimit);
        Check("Budgets from the income", DataStore.Budgets.Count >= 6 && budgeted is > 3800m and <= 4100m && DataStore.Budgets.Items.Any(b => b.Category == "Savings"), $"{DataStore.Budgets.Count} budgets, {budgeted}");

        await Step("Tick rent and car insurance", () => { gs.TickBill("Rent", "1450", "1"); gs.TickBill("Car insurance", "120", "15"); gs.Advance(); });
        var rent = DataStore.Bills.Items.FirstOrDefault(b => b.Name == "Rent");
        Check("Bills created, repeating", DataStore.Bills.Count == 2 && rent is { Amount: 1450m, Category: "Housing", Repeats: BillSchedule.Monthly } && rent.NextDue.Day == 1 && rent.NextDue >= DateTime.Today,
            string.Join(", ", DataStore.Bills.Items.Select(b => $"{b.Name} {b.Amount} {b.NextDue:d}")));

        await Step("Type a student loan with its monthly payment", () =>
        {
            Named<System.Windows.Controls.TextBox>(gs, "Amount owed")!.Text = "18420";
            Named<System.Windows.Controls.TextBox>(gs, "Monthly payment")!.Text = "300";
            Named<System.Windows.Controls.TextBox>(gs, "Payment due day")!.Text = "15";
            gs.Advance();
        });
        var loan = DataStore.Debts.Items.FirstOrDefault(d => d.Name == "Student loan");
        Check("Debt added with a linked monthly bill", loan is { Balance: 18420m, MinimumPayment: 300m } && DebtLink.BillFor(loan) is { Amount: 300m, Repeats: BillSchedule.Monthly } lb && lb.NextDue.Day == 15);
        Check("On the protection step", gs.CurrentStep == "Protect", gs.CurrentStep);
        await Step("Not now → all set", () => gs.Advance());
        Check("All set step", gs.CurrentStep == "Done", gs.CurrentStep);
        await Step("Go to my dashboard", () => gs.Close());
        Check("Get started closed and remembered", window.Setup == null && DataStore.Prefs.SetupDone && ModuleCatalog.Dashboard.IsSelected);

        await Step("Run Get started again from Settings", () => window.ShowGetStarted());
        Check("Get started can run again", window.Setup?.CurrentStep == "Welcome");
        await Step("Back on the bills step keeps them", () => { window.Setup!.GoToStep("Bills"); window.Setup!.Advance(); });
        Check("Coming back doesn't duplicate bills", DataStore.Bills.Items.Count(b => b.Name == "Rent") == 1);
        await Step("Skip setup", () => window.CloseGetStarted());

        await Step("Delete all data (this PC)", () => DataStore.EraseAll(syncDeletes: false));
        Check("Delete all data empties WillowWallet", DataStore.IsEmpty && DataStore.Bills.Count == 0 && DataStore.Debts.Count == 0 && !DataStore.Prefs.SetupDone);
        Check("Categories are back to the defaults", Categories.Spending.Contains("Groceries"));
        await AllPages(window, "after deleting all data");
    }

    /// <summary>Repeating income: schedules, paydays added automatically, and asking when the amount changes.</summary>
    private static async Task IncomeFlow()
    {
        var today = DateTime.Today;
        var nov1 = new DateTime(2026, 11, 1);
        Check("Twice a month: 1st → 15th → 1st", IncomeSchedule.Next(nov1, IncomeSchedule.TwiceAMonth, 1, 15) == new DateTime(2026, 11, 15)
            && IncomeSchedule.Next(new DateTime(2026, 11, 15), IncomeSchedule.TwiceAMonth, 1, 15) == new DateTime(2026, 12, 1));
        Check("Monthly on the 31st keeps the day", IncomeSchedule.Next(new DateTime(2027, 1, 31), IncomeSchedule.Monthly, 31, 15) == new DateTime(2027, 2, 28)
            && IncomeSchedule.Next(new DateTime(2027, 2, 28), IncomeSchedule.Monthly, 31, 15) == new DateTime(2027, 3, 31));
        Check("Every 2 weeks is 26 paychecks a year", Math.Round(IncomeSchedule.MonthlyAmount(new IncomeSource { Amount = 2000m, Repeats = IncomeSchedule.EveryTwoWeeks }), 2) == 4333.33m);

        IncomeSource? job = null, side = null;
        List<Transaction> added = [];
        await Step("Same-amount paycheck that started 4 weeks ago", () =>
        {
            job = new IncomeSource { Name = "Acme paycheck", Amount = 1850m, Repeats = IncomeSchedule.EveryTwoWeeks, NextDate = today.AddDays(-28), Account = "Checking" };
            side = new IncomeSource { Name = "DoorDash", Varies = true, AutoAdd = false, Repeats = IncomeSchedule.Weekly, NextDate = today.AddDays(-1), Category = "Side income", Account = "Checking" };
            DataStore.Income.Add(job);
            DataStore.Income.Add(side);
            added = Paydays.AddDue(today);
        });
        Check("Missed paydays are caught up automatically", added.Count == 3 && added.All(t => t.Amount == 1850m && t.Category == "Paycheck") && job!.NextDate == today.AddDays(14),
            $"{added.Count} added, next {job?.NextDate:d}");
        Check("Running it again adds nothing", Paydays.AddDue(today).Count == 0);
        Check("Income that changes is asked about, not added", Paydays.Asking(today).SequenceEqual([side!]) && !DataStore.Transactions.Items.Any(t => t.Payee == "DoorDash"));
        await Step("Enter what came in", () => Paydays.Receive(side!, 132.75m, side!.NextDate));
        Check("Entered amount recorded on payday", DataStore.Transactions.Items.Any(t => t.Payee == "DoorDash" && t.Amount == 132.75m && t.Category == "Side income" && t.Date == today.AddDays(-1))
            && side!.NextDate == today.AddDays(6) && Paydays.Asking(today).Count == 0);
        Check("Typical amount comes from what came in", IncomeSchedule.Typical(side!) == 132.75m);
        Check("Expected income per month", Math.Round(Paydays.ExpectedMonthly(), 2) == Math.Round(1850m * 26 / 12 + 132.75m * 52 / 12, 2), Paydays.ExpectedMonthly().ToString("0.00"));
        Check("Next paydays this month are listed", IncomeSchedule.Paydays(job!, today, today.AddDays(30)).Count() is 2 or 3);
        await Step("Clean up", () => DataStore.EraseAll(syncDeletes: false));
    }

    /// <summary>
    /// Forecast, sinking funds and the calculator. The same example as the phone app's tests (mobile/test/forecast_test.dart):
    /// Nov 10, 2026, $900 in checking, $1,500 every two weeks from Nov 14, $1,200 rent on the 20th, a $60 internet bill
    /// that's overdue, $300 left in a $400 Groceries budget, and a $360 car registration fund due Mar 10.
    /// </summary>
    private static async Task ForecastFlow(MainWindow window)
    {
        var today = new DateTime(2026, 11, 10);
        await Step("Forecast example", () =>
        {
            DataStore.Accounts.Add(new Account { Name = "Checking", Type = AccountTypes.Checking, StartingBalance = 1000m, BalanceDate = new DateTime(2026, 11, 1) });
            DataStore.Transactions.Add(new Transaction { Payee = "Kroger", Category = "Groceries", Account = "Checking", Date = new DateTime(2026, 11, 5), Amount = -100m });
            DataStore.Income.Add(new IncomeSource { Name = "Paycheck", Amount = 1500m, Repeats = IncomeSchedule.EveryTwoWeeks, NextDate = new DateTime(2026, 11, 14), Account = "Checking", Day1 = 14 });
            DataStore.Bills.Add(new Bill { Name = "Rent", Amount = 1200m, NextDue = new DateTime(2026, 11, 20), DueDay = 20, Category = "Housing" });
            DataStore.Bills.Add(new Bill { Name = "Internet", Amount = 60m, NextDue = new DateTime(2026, 11, 5), DueDay = 5, Category = "Utilities" });
            DataStore.Budgets.Add(new Budget { Category = "Groceries", MonthlyLimit = 400m, StartMonth = new DateTime(2026, 11, 1) });
            DataStore.Goals.Add(new Goal { Name = "Car registration", Target = 360m, TargetDate = new DateTime(2027, 3, 10), IsFund = true });
        });
        var f = Forecast.Run(today);
        string Days() => string.Join(", ", f.Days.Take(5).Select(d => d.Expected.ToString("0.00")));
        Check("Forecast: runs to the end of the month from $900", f.End == new DateTime(2026, 11, 30) && f.Start == 900m && f.Days.Count == 21, $"{f.End:d} {f.Start} {f.Days.Count}");
        Check("Forecast: ends $2,250 over (the same as the phone app)", f.EndExpected == 2250m && f.EndLow == f.EndHigh && f.Verdict == ForecastVerdict.Over, f.EndExpected.ToString("0.00"));
        Check("Forecast: today is $900 − $60 − a day of groceries", f.Days[0].Expected == 825.71m, Days());
        Check("Forecast: lowest on Nov 13, the day before payday", f.LowPoint.Date == new DateTime(2026, 11, 13) && f.LowPoint.Expected == 782.86m && f.NextPayday == new DateTime(2026, 11, 14),
            $"{f.LowPoint.Date:d} {f.LowPoint.Expected}");
        Check("Forecast: safe to spend", f.SafeToSpend == 782.86m, f.SafeToSpend.ToString());
        Check("Forecast: what moves the number", string.Join("; ", f.Drivers().Select(d => $"{d.Label} {d.Amount:0}")) ==
            "Paycheck: Paycheck 3000; Bill: Rent -1200; Budget: Groceries -300; Sinking fund: Car registration -90; Bill: Internet -60",
            string.Join("; ", f.Drivers().Select(d => $"{d.Label} {d.Amount:0}")));
        var tires = Forecast.Run(today, whatIfs: [new WhatIf("Tires", -500m, new DateTime(2026, 11, 12))]);
        Check("Forecast: a what-if expense", tires.EndExpected == 1750m && tires.LowPoint.Expected == 282.86m, $"{tires.EndExpected} {tires.LowPoint.Expected}");
        var rent = DataStore.Bills.Items.First(b => b.Name == "Rent");
        Check("Forecast: skipping a bill", Forecast.Run(today, skipBills: new HashSet<string> { rent.Id }).EndExpected == 3450m);
        Check("Forecast: changing a budget", Forecast.Run(today, budgetChanges: new Dictionary<string, decimal> { ["Groceries"] = 200m }).EndExpected == 2450m);
        DataStore.Prefs.ForecastTarget = 3000m;
        var shortOf = Forecast.Run(today);
        Check("Forecast: short of a $3,000 target", shortOf.Verdict == ForecastVerdict.Under && shortOf.Difference == -750m && shortOf.SafeToSpend == 0);
        DataStore.Prefs.ForecastTarget = 0;
        DataStore.Prefs.ForecastHorizon = "30";
        var f30 = Forecast.Run(today);
        Check("Forecast: 30 days ahead", f30.End == new DateTime(2026, 12, 10) && f30.Events.Count(e => e.Kind == "income") == 2);
        DataStore.Prefs.ForecastHorizon = "Month";
        DataStore.Prefs.Save();

        await Step("Forecast page", () => window.GoTo("forecast"));
        await Step("Goals page with a sinking fund", () => window.GoTo("goals"));
        await Step("Bills calendar with balances", () => { BillsView.ShowCalendar = true; window.GoTo("bills"); });
        BillsView.ShowCalendar = false;
        await Step("Dashboard with the forecast card", () => window.GoTo("dashboard"));

        var fund = new Goal { Name = "Registration", Target = 360m, Saved = 300m, TargetDate = new DateTime(2026, 11, 15), IsFund = true };
        await Step("Pay a sinking fund", () => { DataStore.Goals.Add(fund); GoalMath.UseFund(fund, new DateTime(2026, 11, 16)); });
        Check("Sinking fund: paying it starts saving for next year", fund.Saved == 0 && fund.TargetDate == new DateTime(2027, 11, 15) && fund.History.Last().Amount == -300m
            && GoalMath.MonthlyNeeded(fund, new DateTime(2026, 11, 16)) == 30m, $"{fund.Saved} {fund.TargetDate:d}");
        Check("Sinking fund: kind and schedule are saved", DataStore.Goals.Items.First(g => g.Id == fund.Id).IsFund && fund.Repeats == FundSchedule.Yearly);

        var c = new CalcEngine();
        foreach (var k in "12") c.Digit(k.ToString());
        c.Op("+"); c.Digit("3"); c.Digit("0"); c.Op("+");
        bool chained = c.Display == "42";
        c.Digit("8"); c.Evaluate();
        Check("Calculator: 12 + 30 + 8 = 50", chained && c.Display == "50" && c.History == "42 + 8 =", c.Display);
        c.Op("+"); c.Digit("1"); c.Digit("0"); c.Percent(); c.Evaluate();
        Check("Calculator: 50 + 10% = 55", c.Display == "55", c.Display);
        c.Clear(); c.Digit("7"); c.Op("÷"); c.Digit("0"); c.Evaluate();
        Check("Calculator: dividing by zero says so", c.IsError);
        c.Clear(); c.Digit("0"); c.Digit("."); c.Digit("1"); c.Op("+"); c.Digit("0"); c.Digit("."); c.Digit("2"); c.Evaluate();
        Check("Calculator: 0.1 + 0.2 = 0.3", c.Display == "0.3", c.Display);
        await Step("Open the calculator (Ctrl+K)", window.ToggleCalculator);
        await Step("Type 1240 + 360 =", () => { foreach (var k in new[] { "1", "2", "4", "0", "+", "3", "6", "0", "=" }) window.Calculator?.Press(k); });
        Check("Calculator flyout opens from the sidebar and adds up", window.Calculator?.Display == "1600", window.Calculator?.Display ?? "closed");
        await Step("Close the calculator", window.ToggleCalculator);
        Check("Calculator closes again", window.Calculator == null);

        await Step("Clean up", () => DataStore.EraseAll(syncDeletes: false));
    }

    /// <summary>"Subscriptions you could cut" on the Budgets page, and skipping a suggestion for a month or for good.</summary>
    private static async Task CutsFlow(MainWindow window)
    {
        var today = DateTime.Today;
        var month = Budgeting.MonthStart(today);
        await Step("Three streaming services, one with a price increase", () =>
        {
            DataStore.Bills.Add(new Bill { Name = "Netflix", Amount = 15.49m, NextDue = today.AddDays(5), DueDay = today.AddDays(5).Day, Category = "Subscriptions", Account = "Visa" });
            DataStore.Bills.Add(new Bill { Name = "Hulu", Amount = 17.99m, NextDue = today.AddDays(9), DueDay = today.AddDays(9).Day, Category = "Entertainment", Account = "Visa" });
            foreach (int m in new[] { 4, 3, 2, 1, 0 })
                DataStore.Transactions.Add(new Transaction { Payee = "DISNEY PLUS 8842", Category = "Subscriptions", Account = "Visa", Amount = m >= 3 ? -10.99m : -13.99m, Date = today.AddDays(-2 - 30 * m) });
            DataStore.Bills.Add(new Bill { Name = "Amazon Prime", Amount = 139m, Repeats = BillSchedule.Yearly, NextDue = today.AddDays(20), DueDay = today.AddDays(20).Day, Category = "Subscriptions", Account = "Visa" });
            DataStore.Bills.Add(new Bill { Name = "Dropbox", Amount = 11.99m, NextDue = today.AddDays(12), DueDay = today.AddDays(12).Day, Category = "Subscriptions", Account = "Visa" });
            foreach (int m in new[] { 5, 3, 1 })
                DataStore.Transactions.Add(new Transaction { Payee = "Dropbox", Category = "Subscriptions", Account = "Visa", Amount = m == 5 ? -9.99m : -11.99m, Date = today.AddDays(-30 * m) });
            DataStore.Bills.Add(new Bill { Name = "Car insurance", Amount = 120m, NextDue = today.AddDays(3), DueDay = today.AddDays(3).Day, Category = "Transportation", Account = "Visa" });
        });
        var subs = SubscriptionCuts.All(today);
        Check("Subscriptions found on Bills and in Transactions", subs.Count == 5 && subs.Any(x => x.Name == "Hulu") && subs.Any(x => x.Guess != null && x.Key.StartsWith("disney")) && !subs.Any(x => x.Name == "Car insurance"),
            string.Join(", ", subs.Select(x => x.Name)));
        var cuts = SubscriptionCuts.Suggest(month, today);
        string Names(List<CutSuggestion> c) => string.Join(", ", c.Select(x => $"{x.Sub.Name}:{x.Priority}"));
        Check("Overlapping streaming: keeps the priciest, suggests the others", cuts.Any(c => c.Sub.Name == "Netflix" && c.Priority == 40) && !cuts.Any(c => c.Sub.Name == "Hulu" && c.Priority == 40)
            && cuts.Any(c => c.Key.StartsWith("disney") && c.Priority == 40), Names(cuts));
        Check("Yearly renewal soon is suggested", cuts.Any(c => c.Sub.Name == "Amazon Prime" && c.Priority == 30 && c.YearlySaving == 139m), Names(cuts));
        Check("Hulu suggested for what it costs a year", cuts.Any(c => c.Sub.Name == "Hulu" && c.Priority == 10), Names(cuts));
        Check("Overlap comes first", cuts[0].Priority == 40, Names(cuts));
        Check("Price increase is noticed", cuts.Any(c => c.Sub.Name == "Dropbox" && c.Priority == 35 && c.Reason.Contains("$9.99") && c.Reason.Contains("$11.99")), Names(cuts));
        Check("A charge whose price went up is still found", SubscriptionFinder.Find(today).Any(g => g.Payee.StartsWith("DISNEY") && g.Amount == 13.99m));

        await Step("Budgets page with suggestions", () => { MonthSwitcher.Current = month; window.GoTo("budgets"); });
        await Step("Skip Netflix for this month", () => SubscriptionCuts.Skip("netflix", month));
        Check("Skipped this month", !SubscriptionCuts.Suggest(month, today).Any(c => c.Key == "netflix"));
        Check("Back next month", SubscriptionCuts.Suggest(month.AddMonths(1), today).Any(c => c.Key == "netflix"));
        await Step("Don't suggest Amazon Prime again", () => SubscriptionCuts.Skip("amazon prime", null));
        Check("Skipped for good", !SubscriptionCuts.Suggest(month.AddMonths(1), today).Any(c => c.Key == "amazon prime") && !SubscriptionCuts.Suggest(month.AddMonths(6), today).Any(c => c.Key == "amazon prime"));
        Check("Skips are saved", DataStore.Prefs.SkippedCuts.Contains("amazon prime|forever") && DataStore.Prefs.SkippedCuts.Contains($"netflix|{month:yyyy-MM}"));
        await Step("I canceled Hulu", () => SubscriptionCuts.MarkCanceled(SubscriptionCuts.All(today).First(x => x.Name == "Hulu")));
        Check("Canceled: off Bills and not suggested", !DataStore.Bills.Items.Any(b => b.Name == "Hulu") && !SubscriptionCuts.Suggest(month.AddMonths(1), today).Any(c => c.Key == "hulu"));
        await Step("I canceled Disney+ (found in Transactions)", () => SubscriptionCuts.MarkCanceled(SubscriptionCuts.All(today).First(x => x.Key.StartsWith("disney"))));
        Check("Canceled charge isn't found again", !SubscriptionFinder.Find(today).Any(g => g.Payee.StartsWith("DISNEY")) && SubscriptionCuts.All(today).Count == 3);
        Check("One streaming service left: no overlap", !SubscriptionCuts.Suggest(month.AddMonths(1), today).Any(c => c.Priority == 40));
        await Step("Show skipped", SubscriptionCuts.ResetSkips);
        Check("Skipped ones come back", SubscriptionCuts.Suggest(month, today).Any(c => c.Key == "amazon prime"));
        await Step("Budgets page after", () => window.GoTo("budgets"));
        await Step("Clean up", () => DataStore.EraseAll(syncDeletes: false));
        Check("Delete all data clears skips", DataStore.Prefs.SkippedCuts.Count == 0);
    }

    public static async Task<int> RunAsync(MainWindow window)
    {
        Report.AppendLine($"WillowWallet {App.VersionText} self-test {DateTime.Now:u}");
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

        await GetStartedFlow(window);
        await IncomeFlow();
        await CutsFlow(window);
        await ForecastFlow(window);

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

        // ----- Bills that pay down debts -----
        Debt? loan = null;
        Bill? loanBill = null, insurance = null;
        await Step("Link a student loan to a recurring monthly bill and pay it", () =>
        {
            loan = new Debt { Name = "Student loan", Type = "Student loan", Balance = 550m, OriginalBalance = 5000m, InterestRate = 5m, MinimumPayment = 300m };
            DataStore.Debts.Add(loan);
            loanBill = DebtLink.CreateBill(loan, today, "Checking");
            insurance = new Bill { Name = "Car insurance", Amount = 120m, NextDue = today, DueDay = today.Day, Category = "Transportation", Account = "Checking" };
            DataStore.Bills.Add(insurance);
            BillSchedule.MarkPaid(loanBill, today);
        });
        Check("Debt bill is monthly, in Debt payments, at the minimum payment",
            loanBill != null && loanBill.Repeats == BillSchedule.Monthly && loanBill.Category == Categories.DebtPayments && loanBill.Amount == 300m && DebtLink.BillFor(loan!) == loanBill);
        Check("Paying the bill lowers the debt and moves the bill to next month",
            loan != null && loan.Balance == 250m && loan.Payments.Count == 1 && loanBill!.NextDue == BillSchedule.Next(today, BillSchedule.Monthly, today.Day) && !loanBill.IsDone,
            loan == null ? "" : $"balance {loan.Balance}, next {loanBill?.NextDue:d}");
        Check("Bill payment is recorded once, as a debt payment",
            DataStore.Transactions.Items.Count(t => t.Payee == "Student loan" && t.Amount == -300m && t.Category == Categories.DebtPayments) == 1);
        Check("Insurance stays an ordinary bill", insurance != null && !insurance.PaysDebt && DebtLink.DebtFor(insurance) == null);
        await Step("Pay the last of the loan through its bill", () => BillSchedule.MarkPaid(loanBill!, today));
        Check("Last payment is only what's left", loan != null && loan.IsPaidOff && DataStore.Transactions.Items.Any(t => t.Payee == "Student loan" && t.Amount == -250m));
        Check("The bill ends when the debt is paid off", loanBill != null && loanBill.IsDone && BillSchedule.Status(loanBill, today) == BillStatus.Paid);
        Debt? car = null;
        Bill? carBill = null;
        await Step("Record a car loan payment on the Debts page that counts as its bill", () =>
        {
            car = new Debt { Name = "Car loan", Type = "Car loan", Balance = 9000m, OriginalBalance = 12000m, InterestRate = 6.9m, MinimumPayment = 325m };
            DataStore.Debts.Add(car);
            carBill = DebtLink.CreateBill(car, today.AddDays(3), "Checking");
            DebtPaymentDialog.Apply(car, 325m, today, "Checking", addTransaction: true);
            if (DebtLink.LooksLikeBillPayment(carBill, today)) DebtLink.Advance(carBill, today, car);
        });
        Check("A payment near the due date moves the bill on", carBill != null && carBill.NextDue == BillSchedule.Next(today.AddDays(3), BillSchedule.Monthly, today.AddDays(3).Day) && car!.Balance == 8675m);
        await Step("Delete the car loan", () => { DebtLink.Unlink(car!); DataStore.Debts.Delete(car!); });
        Check("Deleting a debt keeps its bill as a regular bill", carBill != null && DataStore.Bills.Items.Contains(carBill) && !carBill.PaysDebt);

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
        Check("Goals, bills and debts saved to disk", reloadedGoals.Items.Count == 2 && reloadedBills.Items.Count == 6 && reloadedDebts.Items.Count == 3
            && reloadedBills.Items.Any(b => b.DebtId == loan!.Id && b.IsDone)
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
        Check("Every section works (none left coming soon)", ModuleCatalog.Sidebar.All(m => !m.IsComingSoon));
        Check("Section keys are unique",
            ModuleCatalog.Sidebar.Select(m => m.Key).Distinct().Count() == ModuleCatalog.Sidebar.Count);
        Check("Version is set", App.VersionText != "0.0.0", App.VersionText);
        await Step("Toast", () => window.ShowToast("Self-test toast"));
        await Step("Unknown page falls back to the Dashboard", () => window.GoTo("no-such-page"));
        Check("Dashboard selected after unknown page", ModuleCatalog.Dashboard.IsSelected);

        await Security(window);
        await Sync(window);

        Report.AppendLine(_failed == 0 ? "ALL PASSED" : $"{_failed} FAILED");
        try { File.WriteAllText(OutputFile!, Report.ToString()); } catch { /* ignore */ }
        try { Directory.Delete(TempDataFolder, recursive: true); } catch { /* ignore */ }
        return _failed;
    }
}
