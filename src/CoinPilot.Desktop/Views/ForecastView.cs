using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>The Forecast page: where your money is headed, the low point, safe to spend, what moves the number, and what-ifs.</summary>
public sealed class ForecastView : DataPage
{
    // What-ifs live on the page only: nothing here is saved
    private static readonly List<WhatIf> WhatIfs = [];
    private static readonly HashSet<string> SkipBills = [];
    private static readonly Dictionary<string, decimal> BudgetChanges = new(StringComparer.OrdinalIgnoreCase);
    private static bool Trying => WhatIfs.Count > 0 || SkipBills.Count > 0 || BudgetChanges.Count > 0;

    public static string Glyph => "";

    protected override void Build()
    {
        GoalMath.RefreshLinked();
        var now = DateTime.Today;
        var f = Forecast.Run(now, whatIfs: WhatIfs, skipBills: SkipBills, budgetChanges: BudgetChanges);
        var plain = Trying ? Forecast.Run(now) : f;
        var low = f.LowPoint;
        string verdictBrush = f.Verdict switch { ForecastVerdict.Under => "Brush.Danger", ForecastVerdict.Over => "Brush.Positive", _ => "Brush.Accent" };

        Add(PageKit.Header(Glyph, "Forecast", "Where your money is headed, from your paychecks, bills, budgets and plans.",
            PageKit.ChoiceChips(Forecast.Horizons.Select(Forecast.HorizonLabel), Forecast.HorizonLabel(DataStore.Prefs.ForecastHorizon), pick =>
            {
                DataStore.Prefs.ForecastHorizon = Forecast.Horizons.First(h => Forecast.HorizonLabel(h) == pick);
                DataStore.Prefs.Save();
                Rebuild();
            })));

        if (f.Accounts.Count == 0)
            Add(PageKit.Card(Ui.Text("Add your checking account (and cash or payment apps) on the Accounts page, so the forecast starts from what you really have.", muted: true)
                .Also(t => t.TextWrapping = TextWrapping.Wrap)));

        // Headline and chart
        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = Forecast.VerdictText(f), FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 24, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap }
            .Also(t => t.SetResourceReference(TextBlock.ForegroundProperty, verdictBrush)));
        string range = f.EndLow != f.EndHigh ? $" · likely {Money.Format(f.EndLow, cents: false)} to {Money.Format(f.EndHigh, cents: false)}" : "";
        head.Children.Add(Ui.Text($"Expected {Money.Format(f.EndExpected, cents: false)} on {f.End:MMM d}{range}", muted: true).Also(t => t.Margin = new Thickness(0, 4, 0, 0)));
        if (Trying)
            head.Children.Add(Ui.Text($"{Money.Signed(f.EndExpected - plain.EndExpected)} with your what-ifs", weight: FontWeights.SemiBold,
                brushKey: f.EndExpected >= plain.EndExpected ? "Brush.Positive" : "Brush.Danger").Also(t => t.Margin = new Thickness(0, 4, 0, 0)));
        var hover = Ui.Text("Point at the chart to see a day", muted: true, size: 12.5).Also(t => t.Margin = new Thickness(0, 14, 0, 6));
        head.Children.Add(hover);
        head.Children.Add(new ForecastChart(f, 220, hover));
        var axis = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        axis.Children.Add(Ui.Text(f.End.ToString("MMM d"), muted: true, size: 11.5).Also(t => DockPanel.SetDock(t, Dock.Right)));
        axis.Children.Add(Ui.Text("Today", muted: true, size: 11.5));
        head.Children.Add(axis);
        var tags = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        tags.Children.Add(PageKit.Pill($"{f.Confidence} confidence", "Brush.TextMuted").Also(p => p.Margin = new Thickness(0, 0, 8, 0)));
        tags.Children.Add(PageKit.Pill($"From {Money.Format(f.Start, cents: false)} today", "Brush.TextMuted").Also(p => p.Margin = new Thickness(0, 0, 8, 0)));
        if (f.NextPayday is DateTime pay) tags.Children.Add(PageKit.Pill($"Next payday {pay:MMM d}", "Brush.TextMuted"));
        head.Children.Add(tags);
        Add(PageKit.Card(head));

        Add(PageKit.KpiRow(
            PageKit.Kpi("Safe to spend", Money.Format(f.SafeToSpend, cents: false),
                f.NextPayday is DateTime np ? $"Beyond your budgets until {np:MMM d}" : "Beyond your budgets", hero: true),
            PageKit.Kpi("Lowest point", Money.Format(low.Expected, cents: false), low.Date == now ? "Today" : low.Date.ToString("ddd, MMM d"),
                valueBrush: low.Expected < 0 ? "Brush.Danger" : null),
            PageKit.Kpi("Expected at the end", Money.Format(f.EndExpected, cents: false), f.End.ToString("MMM d")),
            PageKit.Kpi("Aim to have at least", Money.Format(f.Target, cents: false), f.Target == 0 ? "Just don't run out" : "Your cushion")));

        if (low.Expected < Math.Max(0, f.Target) && low.Date < f.End)
        {
            string before = f.NextPayday is DateTime p2 && low.Date < p2 ? $", {(p2 - low.Date).Days} days before payday" : "";
            var warn = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 18), BorderThickness = new Thickness(1) };
            warn.SetResourceReference(Border.BorderBrushProperty, "Brush.Warn");
            warn.Child = Ui.Text($"⚠  Your balance dips to {Money.Format(low.Expected, cents: false)} on {low.Date:MMM d}{before}. Moving a bill or spending a little less before then keeps you above {Money.Format(Math.Max(0, f.Target), cents: false)}.",
                weight: FontWeights.SemiBold).Also(t => t.TextWrapping = TextWrapping.Wrap);
            Add(warn);
        }

        var cols = Ui.Columns(1, 1);
        cols.Margin = new Thickness(0, 0, -18, 0);

        // What moves the number
        var drivers = new StackPanel();
        var list = f.Drivers();
        if (list.Count == 0) drivers.Children.Add(Ui.Text("Add your income, bills and budgets to see where you're headed.", muted: true));
        foreach (var d in list)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            row.Children.Add(Ui.Text(Money.Signed(d.Amount), weight: FontWeights.SemiBold, brushKey: d.Amount >= 0 ? "Brush.Positive" : "Brush.Text").Also(t => DockPanel.SetDock(t, Dock.Right)));
            row.Children.Add(Ui.Text(d.Label));
            drivers.Children.Add(row);
        }
        cols.Children.Add(PageKit.Card(drivers, "What moves the number").At(0, new Thickness(0, 0, 18, 18)));

        // Coming up
        var coming = new StackPanel();
        foreach (var e in f.Events.Where(e => e.Kind is not ("budget" or "other")).Take(10))
        {
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(Ui.Text(Money.Signed(e.Amount), weight: FontWeights.SemiBold, brushKey: e.Amount >= 0 ? "Brush.Positive" : "Brush.Text").Also(t => t.HorizontalAlignment = HorizontalAlignment.Right));
            right.Children.Add(Ui.Text(Money.Format(f.DayOf(e.Date)?.Expected ?? 0, cents: false), muted: true, size: 11.5).Also(t => t.HorizontalAlignment = HorizontalAlignment.Right));
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            var left = new StackPanel();
            left.Children.Add(Ui.Text(e.Label));
            left.Children.Add(Ui.Text(e.Date == now ? "Today" : e.Date.ToString("ddd, MMM d"), muted: true, size: 12));
            row.Children.Add(left);
            coming.Children.Add(row);
        }
        if (coming.Children.Count == 0) coming.Children.Add(Ui.Text("Nothing scheduled.", muted: true));
        cols.Children.Add(PageKit.Card(coming, "Coming up").At(1, new Thickness(0, 0, 18, 18)));
        Add(cols);

        // What if
        var what = new StackPanel();
        what.Children.Add(Ui.Text("Try changes here. Nothing is saved.", muted: true).Also(t => t.Margin = new Thickness(0, 0, 0, 10)));
        var chips = new WrapPanel();
        foreach (var w in WhatIfs.ToList())
            chips.Children.Add(Removable($"{w.Label} {Money.Signed(w.Amount)} · {w.Date:MMM d}", () => WhatIfs.Remove(w)));
        foreach (var id in SkipBills.ToList())
            chips.Children.Add(Removable("Skip " + (DataStore.Bills.Find(id)?.Name ?? "bill"), () => SkipBills.Remove(id)));
        foreach (var (cat, amount) in BudgetChanges.ToList())
            chips.Children.Add(Removable($"{cat} budget {Money.Format(amount, cents: false)}", () => BudgetChanges.Remove(cat)));
        if (chips.Children.Count > 0) what.Children.Add(chips);
        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(PageKit.Ghost("", "One-time expense", (_, _) => AddWhatIf(spend: true)).Also(b => b.Margin = new Thickness(0, 0, 8, 8)));
        buttons.Children.Add(PageKit.Ghost("", "Extra money in", (_, _) => AddWhatIf(spend: false)).Also(b => b.Margin = new Thickness(0, 0, 8, 8)));
        if (DataStore.Bills.Items.Any(b => !b.IsDone))
            buttons.Children.Add(PageKit.Ghost("", "Skip a bill", (_, _) => SkipABill()).Also(b => b.Margin = new Thickness(0, 0, 8, 8)));
        if (DataStore.Budgets.Items.Count > 0)
            buttons.Children.Add(PageKit.Ghost("", "Change a budget", (_, _) => ChangeABudget()).Also(b => b.Margin = new Thickness(0, 0, 8, 8)));
        if (Trying)
            buttons.Children.Add(PageKit.Link("Clear", () => { WhatIfs.Clear(); SkipBills.Clear(); BudgetChanges.Clear(); Rebuild(); }));
        what.Children.Add(buttons);
        Add(PageKit.Card(what, "What if…"));

        // Settings
        var settings = new StackPanel();
        var target = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        target.Children.Add(PageKit.Link("Change", () =>
        {
            var d = new TextPromptDialog("Aim to have at least", "Amount (0 = just don't run out)", Money.Format(DataStore.Prefs.ForecastTarget).TrimStart('$'), "Save",
                v => Money.TryParse(v, out var x) && x >= 0 ? null : "Enter an amount, like 500.");
            if (d.ShowDialog() == true && Money.TryParse(d.Value, out var amount))
            {
                DataStore.Prefs.ForecastTarget = amount;
                DataStore.Prefs.Save();
                Rebuild();
            }
        }).Also(b => DockPanel.SetDock(b, Dock.Right)));
        target.Children.Add(Ui.Text(DataStore.Prefs.ForecastTarget == 0 ? "Aim to have at least $0 (just don't run out)" : $"Aim to have at least {Money.Format(DataStore.Prefs.ForecastTarget, cents: false)}"));
        settings.Children.Add(target);
        settings.Children.Add(Toggle("Count savings accounts too (start from checking, cash and payment apps, plus savings)", DataStore.Prefs.ForecastIncludeSavings,
            v => DataStore.Prefs.ForecastIncludeSavings = v));
        settings.Children.Add(Toggle("Set money aside for goals and sinking funds each month", DataStore.Prefs.ForecastCountSavings, v => DataStore.Prefs.ForecastCountSavings = v));
        settings.Children.Add(Ui.Text("Budgets you usually go over are forecast at what you usually spend, and paychecks that vary use your recent average. The range shows the best and worst of those.",
            muted: true, size: 12.5).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 10, 0, 0); }));
        Add(PageKit.Card(settings, "Settings"));
    }

    private CheckBox Toggle(string text, bool value, Action<bool> set)
    {
        var c = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 4, 0, 4) };
        c.Click += (_, _) => { set(c.IsChecked == true); DataStore.Prefs.Save(); Rebuild(); };
        return c;
    }

    private Border Removable(string text, Action remove)
    {
        var chip = Ui.Chip(text + "   ✕");
        chip.Margin = new Thickness(0, 0, 8, 8);
        chip.Cursor = Cursors.Hand;
        chip.ToolTip = "Remove";
        chip.MouseLeftButtonUp += (_, _) => { remove(); Rebuild(); };
        return chip;
    }

    private void AddWhatIf(bool spend)
    {
        var d = new WhatIfDialog(spend) { Owner = Window.GetWindow(this) };
        if (d.ShowDialog() == true && d.Result != null) { WhatIfs.Add(d.Result); Rebuild(); }
    }

    private void SkipABill()
    {
        var bills = DataStore.Bills.Items.Where(b => !b.IsDone && !SkipBills.Contains(b.Id)).OrderBy(b => b.NextDue).ToList();
        var d = new PickDialog("Skip a bill", "See the forecast without it.", bills.Select(b => $"{b.Name} · {Money.Format(b.Amount)} · next {b.NextDue:MMM d}").ToList()) { Owner = Window.GetWindow(this) };
        if (d.ShowDialog() == true && d.Picked >= 0) { SkipBills.Add(bills[d.Picked].Id); Rebuild(); }
    }

    private void ChangeABudget()
    {
        var budgets = DataStore.Budgets.Items.OrderBy(b => b.Category).ToList();
        var d = new PickDialog("Change a budget", "Try a different amount for this month.",
            budgets.Select(b => $"{b.Category} · {Money.Format(BudgetChanges.GetValueOrDefault(b.Category, b.MonthlyLimit), cents: false)}").ToList()) { Owner = Window.GetWindow(this) };
        if (d.ShowDialog() != true || d.Picked < 0) return;
        var b = budgets[d.Picked];
        var amount = new TextPromptDialog(b.Category + " budget", "Amount this month", BudgetChanges.GetValueOrDefault(b.Category, b.MonthlyLimit).ToString("0.##", CultureInfo.InvariantCulture), "Try it",
            v => Money.TryParse(v, out var x) && x >= 0 ? null : "Enter an amount.") { Owner = Window.GetWindow(this) };
        if (amount.ShowDialog() == true && Money.TryParse(amount.Value, out var value)) { BudgetChanges[b.Category] = value; Rebuild(); }
    }

    /// <summary>The forecast card on the Dashboard: the verdict, a small chart and safe to spend. Null when there's nothing to forecast.</summary>
    public static Border? DashboardCard(Action open)
    {
        if (DataStore.Income.Items.Count == 0 && DataStore.Bills.Items.Count == 0 && DataStore.Budgets.Items.Count == 0) return null;
        var f = Forecast.Run(DateTime.Today);
        string brush = f.Verdict switch { ForecastVerdict.Under => "Brush.Danger", ForecastVerdict.Over => "Brush.Positive", _ => "Brush.Accent" };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = Forecast.VerdictText(f), FontSize = 18, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap }
            .Also(t => t.SetResourceReference(TextBlock.ForegroundProperty, brush)));
        panel.Children.Add(new ForecastChart(f, 70, null) { Margin = new Thickness(0, 10, 0, 10) });
        var line = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var muted = new System.Windows.Documents.Run("Safe to spend ");
        muted.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.TextMuted");
        line.Inlines.Add(muted);
        line.Inlines.Add(new System.Windows.Documents.Run(Money.Format(f.SafeToSpend, cents: false)) { FontWeight = FontWeights.SemiBold });
        var low = f.LowPoint;
        if (low.Expected < Math.Max(0, f.Target))
        {
            var dip = new System.Windows.Documents.Run($"   ·   Dips to {Money.Format(low.Expected, cents: false)} {low.Date:MMM d}") { FontWeight = FontWeights.SemiBold };
            dip.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.Warn");
            line.Inlines.Add(dip);
        }
        panel.Children.Add(line);
        var card = PageKit.Card(panel, "Forecast", PageKit.Link("Open forecast", open));
        card.Cursor = Cursors.Hand;
        card.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not Button) open(); };
        return card;
    }
}

/// <summary>The expected balance each day, the likely range around it, the target line and the low point. Point at it to see a day.</summary>
public sealed class ForecastChart : FrameworkElement
{
    private readonly ForecastResult _f;
    private readonly TextBlock? _hover;
    private int? _pick;

    public ForecastChart(ForecastResult f, double height, TextBlock? hover)
    {
        _f = f;
        _hover = hover;
        Height = height;
        if (hover != null)
        {
            MouseMove += (_, e) => Pick(e.GetPosition(this).X);
            MouseLeave += (_, _) => { _pick = null; hover.Text = "Point at the chart to see a day"; InvalidateVisual(); };
            Cursor = Cursors.Cross;
        }
    }

    private void Pick(double x)
    {
        var days = _f.Days;
        if (days.Count == 0 || ActualWidth <= 0) return;
        int i = Math.Clamp((int)Math.Round(x / ActualWidth * (days.Count - 1)), 0, days.Count - 1);
        _pick = i;
        var d = days[i];
        string range = d.Low != d.High ? $" ({Money.Format(d.Low, cents: false)}–{Money.Format(d.High, cents: false)})" : "";
        _hover!.Text = $"{(d.Date == DateTime.Today ? "Today" : d.Date.ToString("ddd, MMM d"))}: {Money.Format(d.Expected, cents: false)}{range}";
        InvalidateVisual();
    }

    private Brush B(string key) => (Brush)FindResource(key);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var days = _f.Days;
        if (days.Count == 0 || w <= 0) return;
        double lo = (double)days.Min(d => d.Low), hi = (double)days.Max(d => d.High);
        if (_f.Target != 0) { lo = Math.Min(lo, (double)_f.Target); hi = Math.Max(hi, (double)_f.Target); }
        // Zoom in on the balances (zero only shows when the balance gets near it), with a little room above and below
        double pad = Math.Max((hi - lo) * 0.15, 10);
        lo = lo > pad * 2 ? lo - pad : Math.Min(lo, 0) - pad * 0.3;
        hi += pad;
        double span = hi - lo == 0 ? 1 : hi - lo;
        double X(int i) => days.Count == 1 ? w / 2 : i * w / (days.Count - 1);
        double Y(decimal v) => h - 4 - ((double)v - lo) / span * (h - 8);

        double zeroY = Math.Clamp(Y(0), 0, h);
        if (lo < 0) dc.DrawLine(new Pen(B("Brush.Border"), 1), new Point(0, zeroY), new Point(w, zeroY));
        if (_f.Target != 0)
        {
            var dash = new Pen(B("Brush.TextMuted"), 1) { DashStyle = new DashStyle([4, 4], 0) };
            dc.DrawLine(dash, new Point(0, Y(_f.Target)), new Point(w, Y(_f.Target)));
        }

        if (days.Any(d => d.Low != d.High))
        {
            var band = new StreamGeometry();
            using (var g = band.Open())
            {
                g.BeginFigure(new Point(X(0), Y(days[0].High)), true, true);
                for (int i = 1; i < days.Count; i++) g.LineTo(new Point(X(i), Y(days[i].High)), true, false);
                for (int i = days.Count - 1; i >= 0; i--) g.LineTo(new Point(X(i), Y(days[i].Low)), true, false);
            }
            var fill = B("Brush.Accent").Clone();
            fill.Opacity = 0.14;
            dc.DrawGeometry(fill, null, band);
        }

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(new Point(X(0), Y(days[0].Expected)), false, false);
            for (int i = 1; i < days.Count; i++) g.LineTo(new Point(X(i), Y(days[i].Expected)), true, true);
        }
        var accent = new Pen(B("Brush.Accent"), 2.6) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var danger = new Pen(B("Brush.Danger"), 2.6) { LineJoin = PenLineJoin.Round };
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, Math.Max(0, zeroY))));
        dc.DrawGeometry(null, accent, line);
        dc.Pop();
        dc.PushClip(new RectangleGeometry(new Rect(0, Math.Min(h, zeroY), w, Math.Max(0, h - zeroY))));
        dc.DrawGeometry(null, danger, line);
        dc.Pop();

        int li = 0;
        for (int i = 1; i < days.Count; i++) if (days[i].Expected < days[li].Expected) li = i;
        dc.DrawEllipse(B(days[li].Expected < 0 ? "Brush.Danger" : "Brush.Warn"), null, new Point(X(li), Y(days[li].Expected)), 4.5, 4.5);
        dc.DrawEllipse(B("Brush.Accent"), null, new Point(X(days.Count - 1), Y(days[^1].Expected)), 4.5, 4.5);

        if (_pick is int p)
        {
            dc.DrawLine(new Pen(B("Brush.TextMuted"), 1), new Point(X(p), 0), new Point(X(p), h));
            dc.DrawEllipse(B("Brush.Text"), null, new Point(X(p), Y(days[p].Expected)), 5, 5);
        }
    }
}

/// <summary>A one-time expense or extra money to try in the forecast.</summary>
public sealed class WhatIfDialog : DialogShell
{
    private readonly bool _spend;
    private readonly TextBox _name, _amount;
    private readonly DatePicker _date;
    public WhatIf? Result { get; private set; }

    public WhatIfDialog(bool spend)
        : base(spend ? "One-time expense" : "Extra money in", spend ? "New tires, a trip, a gift…" : "A bonus, a tax refund, selling something…",
               allowDelete: false, saveText: "Try it", width: 480)
    {
        _spend = spend;
        _name = AddField("What is it?", new TextBox { Text = "" });
        _amount = AddField("Amount", AmountBox(null), required: true);
        _date = AddField("When", DateBox(DateTime.Today));
        Loaded += (_, _) => _name.Focus();
    }

    protected override string? TrySave()
    {
        if (!ReadAmount(_amount, allowZero: false, out var amount)) return "Enter an amount.";
        string label = _name.Text.Trim().Length > 0 ? _name.Text.Trim() : _spend ? "Expense" : "Extra money";
        Result = new WhatIf(label, _spend ? -amount : amount, _date.SelectedDate?.Date ?? DateTime.Today);
        return null;
    }
}

/// <summary>Pick one item from a short list.</summary>
public sealed class PickDialog : DialogShell
{
    private readonly ListBox _list;
    public int Picked => _list.SelectedIndex;

    public PickDialog(string title, string subtitle, IReadOnlyList<string> items)
        : base(title, subtitle, allowDelete: false, saveText: "Choose", width: 480)
    {
        _list = new ListBox { MinHeight = 120, MaxHeight = 320 };
        foreach (var i in items) _list.Items.Add(i);
        if (items.Count > 0) _list.SelectedIndex = 0;
        _list.MouseDoubleClick += (_, _) => { if (_list.SelectedIndex >= 0) { DialogResult = true; } };
        AddField("", _list);
    }

    protected override string? TrySave() => _list.SelectedIndex < 0 ? "Pick one." : null;
}
