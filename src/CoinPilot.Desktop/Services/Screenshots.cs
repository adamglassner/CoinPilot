using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CoinPilot.Data;
using CoinPilot.Views;

namespace CoinPilot.Services;

/// <summary>
/// <c>CoinPilot.exe --screenshots &lt;folder&gt;</c>: fills a throwaway data folder with realistic sample data, captures every
/// page (light and dark), a few color themes, some dialogs and the lock screen as PNGs, then exits.
/// Used by CI to publish screenshots; never touches real data.
/// </summary>
public static class Screenshots
{
    public static string? OutputFolder { get; set; }
    public static bool IsRunning => OutputFolder != null;

    private const double Width = 1440, Height = 940, Scale = 1.25;
    private static int _failures;

    public static async Task<int> RunAsync(MainWindow window)
    {
        Directory.CreateDirectory(OutputFolder!);
        DemoData.Seed();
        DataStore.Prefs.WelcomeDismissed = true;
        DataStore.Prefs.Save();
        // A fixed window size whatever the runner's screen is (WPF lets a window be larger than the screen)
        window.WindowState = WindowState.Normal;
        window.Left = 0; window.Top = 0;
        window.Width = Width; window.Height = Height;
        await Idle();

        string[] pages = ["dashboard", "transactions", "budgets", "accounts", "debts", "bills", "goals", "reports", "taxes", "settings"];
        int n = 1;
        foreach (var mode in new[] { AppTheme.Light, AppTheme.Dark })
        {
            App.Settings.ColorTheme = ThemePalettes.Default;
            App.ApplyTheme(mode);
            foreach (var page in pages)
            {
                await Show(window, page);
                await Capture(window, $"{n++:00}-{page}-{mode.ToLowerInvariant()}");
                if (page == "bills")
                {
                    BillsView.ShowCalendar = true;
                    await Show(window, page);
                    await Capture(window, $"{n++:00}-bills-calendar-{mode.ToLowerInvariant()}");
                    BillsView.ShowCalendar = false;
                }
                if (page == "reports")
                {
                    ReportsView.Range = ReportsView.YearInReview;
                    ReportsView.ReviewYear = DateTime.Today.Year;
                    await Show(window, page);
                    await Capture(window, $"{n++:00}-reports-year-in-review-{mode.ToLowerInvariant()}");
                    ReportsView.Range = ReportsView.SixMonths;
                }
            }
        }

        // Color themes
        foreach (var (theme, mode) in new[] { ("Ocean", AppTheme.Light), ("Plum", AppTheme.Light), ("Ember", AppTheme.Light), ("Lagoon", AppTheme.Dark), ("Rose", AppTheme.Dark), ("Graphite", AppTheme.Dark) })
        {
            App.Settings.ColorTheme = theme;
            App.ApplyTheme(mode);
            await Show(window, "dashboard");
            await Capture(window, $"{n++:00}-theme-{theme.ToLowerInvariant()}-{mode.ToLowerInvariant()}");
        }
        App.Settings.ColorTheme = ThemePalettes.Default;
        App.ApplyTheme(AppTheme.Light);

        // Dialogs (light, then dark)
        string csv = "Date,Description,Amount\n" + string.Join("\n", Enumerable.Range(1, 8).Select(i =>
            $"{DateTime.Today.AddDays(-i):MM/dd/yyyy},{new[] { "STARBUCKS #1182", "KROGER 0457", "SHELL OIL 5512", "CHIPOTLE 2210", "TARGET T-1034", "NETFLIX.COM", "AMAZON MKTPL", "DUKE ENERGY" }[i - 1]},-{(7.5 + i * 13.27):0.00}"));
        foreach (var mode in new[] { AppTheme.Light, AppTheme.Dark })
        {
            App.ApplyTheme(mode);
            string m = mode.ToLowerInvariant();
            await Show(window, "transactions");
            await CaptureDialog(window, new TransactionDialog(null, DateTime.Today), $"{n++:00}-dialog-add-transaction-{m}");
            await CaptureDialog(window, new TransactionDialog(DataStore.Transactions.Items.First(t => t.IsSplit)), $"{n++:00}-dialog-split-transaction-{m}");
            await CaptureDialog(window, new ImportDialog("Chase-Visa-statement.csv", Importer.ReadCsv(csv)), $"{n++:00}-dialog-import-{m}");
            await Show(window, "budgets");
            await CaptureDialog(window, new BudgetDialog(DataStore.Budgets.Items.First(b => b.Rollover)), $"{n++:00}-dialog-budget-{m}");
            await CaptureDialog(window, new BudgetTemplateDialog(), $"{n++:00}-dialog-budget-template-{m}");
            await Show(window, "bills");
            await CaptureDialog(window, new BillDialog(DataStore.Bills.Items[0]), $"{n++:00}-dialog-bill-{m}");
            await Show(window, "goals");
            await CaptureDialog(window, new GoalDialog(DataStore.Goals.Items[1]), $"{n++:00}-dialog-goal-{m}");
            await Show(window, "settings");
            await CaptureDialog(window, new CategoriesDialog(), $"{n++:00}-dialog-categories-{m}");
            await CaptureDialog(window, new SetLockDialog(change: false), $"{n++:00}-dialog-protect-{m}");
            await CaptureDialog(window, new ConfirmDialog("Restore this backup?", "This replaces everything in CoinPilot with the backup from March 3. Your current data will be lost.", "Restore", danger: true), $"{n++:00}-dialog-confirm-{m}");
        }
        App.ApplyTheme(AppTheme.Light);

        // Lock screen
        DataLock.Enable("482910", "PIN", 15);
        window.LockNow();
        await Idle();
        await Capture(window, $"{n++:00}-lock-screen-light");
        App.ApplyTheme(AppTheme.Dark);
        await Idle();
        await Capture(window, $"{n++:00}-lock-screen-dark");

        try { Directory.Delete(SelfTest.TempDataFolder, recursive: true); } catch { /* ignore */ }
        return _failures;
    }

    private static async Task Idle()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static async Task Show(MainWindow window, string page)
    {
        if (page == "settings") window.ShowSettings();
        else window.GoTo(page);
        await Idle();
    }

    /// <summary>Lays the whole window out at a fixed size (whatever the screen is) and saves it.</summary>
    private static async Task Capture(MainWindow window, string name)
    {
        try
        {
            var root = (FrameworkElement)window.Content;
            await Dispatcher.Yield(DispatcherPriority.Render);
            Save(root, root.ActualWidth, root.ActualHeight, (Brush)Application.Current.FindResource("Brush.Bg"), name);
        }
        catch (Exception ex)
        {
            _failures++;
            File.WriteAllText(Path.Combine(OutputFolder!, name + ".error.txt"), ex.ToString());
        }
    }

    /// <summary>Shows the dialog over the (dimmed) main window and saves the two together, as it looks in the app.</summary>
    private static async Task CaptureDialog(MainWindow window, DialogShell dialog, string name)
    {
        try
        {
            dialog.Show();
            await Idle();
            var root = (FrameworkElement)window.Content;
            double Width = root.ActualWidth, Height = root.ActualHeight;

            var card = dialog.Card;
            var effect = card.Effect;
            card.Effect = null; // the shadow widens the card's bounds; draw our own instead
            card.UpdateLayout();
            double w = card.ActualWidth, h = card.ActualHeight;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle((Brush)Application.Current.FindResource("Brush.Bg"), null, new Rect(0, 0, Width, Height));
                dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, Width, Height));
                double scale = Math.Min(1, (Height - 40) / h);
                var at = new Rect((Width - w * scale) / 2, (Height - h * scale) / 2, w * scale, h * scale);
                for (int i = 1; i <= 6; i++)
                {
                    dc.PushOpacity(0.05);
                    var spread = new Rect(at.X - i * 2, at.Y - i * 2 + 10, at.Width + i * 4, at.Height + i * 4);
                    dc.DrawRoundedRectangle(Brushes.Black, null, spread, 18 + i * 2, 18 + i * 2);
                    dc.Pop();
                }
                dc.DrawRectangle(new VisualBrush(card) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h) }, null, at);
            }
            card.Effect = effect;
            var bitmap = new RenderTargetBitmap((int)(Width * Scale), (int)(Height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(OutputFolder!, name + ".png"));
            encoder.Save(file);
        }
        catch (Exception ex)
        {
            _failures++;
            File.WriteAllText(Path.Combine(OutputFolder!, name + ".error.txt"), ex.ToString());
        }
        finally
        {
            if (dialog is RecoveryKeyDialog r) r.ForceClose = true;
            dialog.Close();
            await Idle();
        }
    }

    private static void Save(Visual element, double w, double h, Brush background, string name)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(background, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
        }
        var bitmap = new RenderTargetBitmap((int)(w * Scale), (int)(h * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(OutputFolder!, name + ".png"));
        encoder.Save(file);
    }
}

/// <summary>Realistic sample data for screenshots: three months of an ordinary household's money.</summary>
public static class DemoData
{
    public static void Seed()
    {
        var today = DateTime.Today;
        var rng = new Random(42);
        decimal R(double min, double max) => Math.Round((decimal)(min + rng.NextDouble() * (max - min)), 2);
        var start = new DateTime(today.Year, today.Month, 1).AddMonths(-3);

        using var _ = DataStore.Batch();

        // Accounts (balances as of the day before the first transaction)
        DataStore.Accounts.AddRange(
        [
            new Account { Name = "Chase Checking", Type = AccountTypes.Checking, Institution = "Chase", StartingBalance = 2840m, BalanceDate = start.AddDays(-1) },
            new Account { Name = "High-Yield Savings", Type = AccountTypes.Savings, Institution = "Ally Bank", StartingBalance = 6900m, BalanceDate = start.AddDays(-1) },
            new Account { Name = "Visa Signature", Type = AccountTypes.CreditCard, Institution = "Chase", StartingBalance = -412m, BalanceDate = start.AddDays(-1) },
            new Account { Name = "Cash", Type = AccountTypes.Cash, StartingBalance = 140m, BalanceDate = start.AddDays(-1) },
            new Account { Name = "Fidelity 401(k)", Type = AccountTypes.Investment, Institution = "Fidelity", StartingBalance = 38450m, BalanceDate = start.AddDays(-1) },
        ]);

        // Categories: a subcategory and a rule
        Categories.Add("Coffee", CategoryKinds.Spending, "Dining out");
        DataStore.Rules.Add(new CategoryRule { Contains = "STARBUCKS", Category = "Coffee" });

        var tx = new List<Transaction>();
        void T(DateTime d, string payee, string cat, string acct, decimal amount, params string[] tags)
        {
            if (d > today) return;
            tx.Add(new Transaction { Date = d, Payee = payee, Category = cat, Account = acct, Amount = amount, Tags = tags.ToList() });
        }

        string[] grocers = ["Kroger", "Aldi", "Fresh Thyme Market", "Trader Joe's"];
        string[] restaurants = ["Chipotle", "Taco Bros", "Panera Bread", "Olive Garden", "Sushi Kame"];
        for (var d = start; d <= today; d = d.AddDays(1))
        {
            int dow = (int)d.DayOfWeek;
            if (d.Day == 1) T(d, "Maple Court Apartments", "Housing", "Chase Checking", -1450m);
            if ((d - start).Days % 14 == 4) T(d, "Paycheck · Acme Corp", "Paycheck", "Chase Checking", 2186.40m);
            if (dow is 2 or 6) T(d, grocers[rng.Next(grocers.Length)], "Groceries", "Visa Signature", -R(38, 128));
            if (dow is 1 or 3 or 5 && rng.NextDouble() < 0.8) T(d, "Starbucks", "Coffee", "Visa Signature", -R(4.5, 7.8));
            if (dow is 4 or 5 or 0 && rng.NextDouble() < 0.6) T(d, restaurants[rng.Next(restaurants.Length)], "Dining out", "Visa Signature", -R(14, 62));
            if (dow == 3) T(d, "Shell", "Transportation", "Visa Signature", -R(34, 49));
            if (d.Day == 18) T(d, "Duke Energy", "Utilities", "Chase Checking", -R(108, 146));
            if (d.Day == 22) T(d, "Comcast Xfinity", "Utilities", "Chase Checking", -79.99m);
            if (d.Day == 12) T(d, "Verizon Wireless", "Utilities", "Chase Checking", -65m);
            if (d.Day == 9) T(d, "Netflix", "Subscriptions", "Visa Signature", -15.49m);
            if (d.Day == 14) T(d, "Spotify P2D84C", "Subscriptions", "Visa Signature", -10.99m);
            if (d.Day == 5) T(d, "Planet Fitness", "Health", "Visa Signature", -24.99m);
            if (d.Day == 3) T(d, "Navient Student Loan", "Debt payments", "Chase Checking", -210m);
            if (d.Day == 15) T(d, "Toyota Financial", "Debt payments", "Chase Checking", -325m);
            if (rng.NextDouble() < 0.09) T(d, new[] { "Target", "Amazon", "Best Buy", "Home Depot" }[rng.Next(4)], "Shopping", "Visa Signature", -R(18, 145));
            if (rng.NextDouble() < 0.05) T(d, new[] { "AMC Theatres", "Steam", "Topgolf" }[rng.Next(3)], "Entertainment", "Visa Signature", -R(16, 68));
            if (d.Day == 20)
            {
                tx.Add(new Transaction { Date = d, Payee = "Visa payment", Category = "Transfer", Account = "Chase Checking", TransferAccount = "Visa Signature", Amount = -R(900, 1300) });
                tx.Add(new Transaction { Date = d, Payee = "Monthly savings", Category = "Transfer", Account = "Chase Checking", TransferAccount = "High-Yield Savings", Amount = -400m });
            }
        }
        var costco = today.AddDays(-6);
        tx.Add(new Transaction
        {
            Date = costco, Payee = "Costco", Category = "Split", Account = "Visa Signature", Amount = -186.42m, Tags = ["bulk"],
            Splits = [new Split { Category = "Groceries", Amount = 121.30m }, new Split { Category = "Shopping", Amount = 65.12m }],
            Note = "Groceries plus a new vacuum filter",
        });
        tx.Add(new Transaction { Date = today.AddDays(-2), Payee = "Delta Air Lines", Category = "Travel", Account = "Visa Signature", Amount = -412.80m, Tags = ["hawaii"] });
        tx.Add(new Transaction { Date = today.AddDays(-9), Payee = "Etsy refund", Category = "Refund", Account = "Visa Signature", Amount = 32.50m });
        DataStore.Transactions.AddRange(tx);

        // Budgets
        var month = Budgeting.MonthStart(today);
        DataStore.Budgets.AddRange(
        [
            new Budget { Category = "Housing", MonthlyLimit = 1450m, StartMonth = month.AddMonths(-3) },
            new Budget { Category = "Groceries", MonthlyLimit = 650m, StartMonth = month.AddMonths(-3) },
            new Budget { Category = "Dining out", MonthlyLimit = 300m, StartMonth = month.AddMonths(-3) },
            new Budget { Category = "Transportation", MonthlyLimit = 200m, StartMonth = month.AddMonths(-3) },
            new Budget { Category = "Utilities", MonthlyLimit = 300m, StartMonth = month.AddMonths(-3) },
            new Budget { Category = "Shopping", MonthlyLimit = 180m, StartMonth = month.AddMonths(-3) },
            new Budget { Category = "Entertainment", MonthlyLimit = 100m, Rollover = true, StartMonth = month.AddMonths(-2) },
            new Budget { Category = "Subscriptions", MonthlyLimit = 40m, StartMonth = month.AddMonths(-3) },
            new Budget { Category = "Travel", MonthlyLimit = 150m, Rollover = true, StartMonth = month.AddMonths(-3) },
        ]);

        // Bills
        DateTime Next(int day) { var d = new DateTime(today.Year, today.Month, Math.Min(day, DateTime.DaysInMonth(today.Year, today.Month))); return d < today.AddDays(-3) ? d.AddMonths(1) : d; }
        DataStore.Bills.AddRange(
        [
            new Bill { Name = "Rent", Amount = 1450m, NextDue = Next(1), DueDay = 1, Category = "Housing", Account = "Chase Checking" },
            new Bill { Name = "Duke Energy", Amount = 130m, NextDue = Next(18), DueDay = 18, Category = "Utilities", Account = "Chase Checking" },
            new Bill { Name = "Comcast Xfinity", Amount = 79.99m, NextDue = Next(22), DueDay = 22, Category = "Utilities", Account = "Chase Checking", AutoPay = true },
            new Bill { Name = "Verizon Wireless", Amount = 65m, NextDue = Next(12), DueDay = 12, Category = "Utilities", Account = "Chase Checking" },
            new Bill { Name = "Netflix", Amount = 15.49m, NextDue = Next(9), DueDay = 9, Category = "Subscriptions", Account = "Visa Signature", AutoPay = true },
            new Bill { Name = "Car insurance", Amount = 618m, NextDue = today.AddDays(5), DueDay = today.AddDays(5).Day, Repeats = BillSchedule.Quarterly, Category = "Transportation", Account = "Chase Checking" },
            new Bill { Name = "Water & sewer", Amount = 46.20m, NextDue = today.AddDays(-2), DueDay = today.AddDays(-2).Day, Category = "Utilities", Account = "Chase Checking" },
            new Bill { Name = "Planet Fitness", Amount = 24.99m, NextDue = Next(5), DueDay = 5, Category = "Health", Account = "Visa Signature", AutoPay = true },
        ]);

        // Goals
        DataStore.Goals.AddRange(
        [
            new Goal { Name = "Emergency fund", Target = 12000m, LinkedAccount = "High-Yield Savings", Note = "Six months of expenses" },
            new Goal { Name = "Hawaii trip", Target = 4500m, Saved = 2150m, TargetDate = today.AddMonths(7), History = [new MoneyEntry { Date = today.AddDays(-30), Amount = 2150m }] },
            new Goal { Name = "New laptop", Target = 1600m, Saved = 1600m },
            new Goal { Name = "Down payment", Target = 40000m, Saved = 8600m, TargetDate = today.AddYears(3) },
        ]);

        // Debts
        DataStore.Debts.AddRange(
        [
            new Debt { Name = "Student loan", Type = "Student loan", Balance = 18420m, OriginalBalance = 26000m, InterestRate = 5.5m, MinimumPayment = 210m },
            new Debt { Name = "Car loan", Type = "Car loan", Balance = 9860m, OriginalBalance = 18500m, InterestRate = 6.9m, MinimumPayment = 325m },
            new Debt { Name = "Store card", Type = DebtTypes.CreditCard, Balance = 640m, OriginalBalance = 1100m, InterestRate = 26.99m, MinimumPayment = 35m },
        ]);
        DataStore.DebtPlan.ExtraPayment = 150m;
        DataStore.DebtPlan.Save();

        // Tax documents (placeholder PDFs)
        int last = today.Year - 1;
        string tmp = Path.Combine(Path.GetTempPath(), "coinpilot-demo.pdf");
        File.WriteAllBytes(tmp, "%PDF-1.4 sample"u8.ToArray());
        foreach (var (year, title, kind) in new[]
                 {
                     (last, "W-2 · Acme Corp", "W-2"), (last, "1099-INT · Ally Bank", "1099"), (last, "1098-E · Student loan interest", "1098"),
                     (last, "Charitable donations", "Charitable donation"), (last - 1, $"{last - 1} federal and state return", "Tax return"),
                     (last - 1, "W-2 · Acme Corp", "W-2"),
                 })
        {
            var doc = new TaxDocument { Year = year, Title = title, Kind = kind, OriginalName = title + ".pdf", Size = 184_000 + title.Length * 1_337 };
            doc.FileName = FileVault.StoreTaxDocument(year, tmp);
            DataStore.TaxDocuments.Add(doc);
        }
        File.Delete(tmp);
    }
}
