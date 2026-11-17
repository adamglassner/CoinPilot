using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>The Receipts page: every receipt you've kept, by year and month (like Taxes), with scanning.</summary>
public sealed class ReceiptsView : DataPage, ISearchablePage
{
    public static int Year { get; set; } = DateTime.Today.Year;
    private static string _search = "";
    private TextBox? _searchBox;

    public static string Glyph => "";

    public void FocusSearch() => _searchBox?.Focus();

    protected override void Build()
    {
        var all = DataStore.Transactions.Items.Where(t => t.Receipt.Length > 0).OrderByDescending(t => t.Date).ToList();
        var years = all.Select(t => t.Date.Year).Append(DateTime.Today.Year).Distinct().OrderByDescending(y => y).ToList();
        if (!years.Contains(Year)) Year = years[0];
        string q = _search.Trim();
        var shown = all.Where(t => t.Date.Year == Year && (q.Length == 0
            || t.Payee.Contains(q, StringComparison.OrdinalIgnoreCase) || t.Category.Contains(q, StringComparison.OrdinalIgnoreCase) || t.Note.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();

        Add(PageKit.Header(Glyph, "Receipts", "Every receipt you've kept, by year. Scan one and WillowWallet reads the store, total and date for you.",
            PageKit.ChoiceChips(years.Select(y => y.ToString()), Year.ToString(), pick => { Year = int.Parse(pick); Rebuild(); })));

        _searchBox = new TextBox { Text = _search, Width = 280, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(_searchBox, "Search receipts");
        _searchBox.TextChanged += (_, _) => { _search = _searchBox.Text; Dispatcher.BeginInvoke(() => { Rebuild(); _searchBox?.Focus(); _searchBox!.CaretIndex = _searchBox.Text.Length; }); };
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var scan = PageKit.Primary("", "Scan a receipt", (_, _) => TransactionDialog.Scan());
        DockPanel.SetDock(scan, Dock.Right);
        bar.Children.Add(scan);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(_searchBox);
        left.Children.Add(Ui.Text(shown.Count == 0 ? "" : $"   {shown.Count} receipt{(shown.Count == 1 ? "" : "s")} · {Money.Format(-shown.Sum(t => t.Amount), cents: false)}", muted: true)
            .Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        bar.Children.Add(left);
        Add(bar);

        if (shown.Count == 0)
        {
            Add(PageKit.Card(Ui.EmptyState(Glyph, q.Length == 0 ? $"No receipts for {Year} yet" : "No receipts match",
                "Scan a receipt and it becomes a transaction with the photo attached, kept here by year. Receipts you attach to transactions show up here too.",
                "Scan a receipt", (_, _) => TransactionDialog.Scan())));
            return;
        }
        foreach (var month in shown.GroupBy(t => t.Date.Month))
        {
            var list = new StackPanel();
            bool first = true;
            foreach (var t in month)
            {
                list.Children.Add(Row(t, first));
                first = false;
            }
            Add(PageKit.Card(list, new DateTime(Year, month.Key, 1).ToString("MMMM", Money.Us), padding: new Thickness(14, 14, 14, 8)));
        }
    }

    private UIElement Row(Transaction t, bool first)
    {
        var g = Ui.Columns(0, 1, 0);
        g.ColumnDefinitions[0].Width = GridLength.Auto;
        g.ColumnDefinitions[2].Width = GridLength.Auto;
        g.Children.Add(Thumb(t).At(0, new Thickness(0, 4, 14, 4)));
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center }.At(1);
        words.Children.Add(Ui.Text(t.Payee.Length == 0 ? "Receipt" : t.Payee, weight: FontWeights.SemiBold));
        words.Children.Add(Ui.Text($"{t.Date:ddd, MMM d} · {Categories.Display(t.Category)} · {t.Account}", muted: true, size: 12.5));
        g.Children.Add(words);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }.At(2);
        right.Children.Add(Ui.Text(Money.Signed(t.Amount), weight: FontWeights.SemiBold).Also(x => { x.VerticalAlignment = VerticalAlignment.Center; x.Margin = new Thickness(0, 0, 16, 0); }));
        right.Children.Add(PageKit.Link("Open", () => TransactionDialog.OpenReceipt(t)));
        right.Children.Add(PageKit.Link("Copy to Taxes", () => CopyToTaxes(t)).Also(b => b.Margin = new Thickness(14, 0, 0, 0)));
        g.Children.Add(right);
        return PageKit.Row(g, $"{t.Payee} {Money.Format(t.Amount)}", () => TransactionDialog.Edit(t), first);
    }

    private static FrameworkElement Thumb(Transaction t)
    {
        var box = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(10), ClipToBounds = true };
        box.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        string path = FileVault.ReceiptPath(t.Receipt);
        try
        {
            if (File.Exists(path) && ReceiptReader.CanRead(path) && !path.EndsWith(".heic", StringComparison.OrdinalIgnoreCase))
            {
                var img = new BitmapImage();
                using var ms = new MemoryStream(DataLock.ReadBytes(path));
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.DecodePixelWidth = 120;
                img.StreamSource = ms;
                img.EndInit();
                img.Freeze();
                box.Background = new System.Windows.Media.ImageBrush(img) { Stretch = System.Windows.Media.Stretch.UniformToFill };
                return box;
            }
        }
        catch { /* unreadable: show the icon */ }
        box.Child = new TextBlock { Text = "", Style = (Style)Application.Current.FindResource("Icon"), FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            .Also(x => x.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent"));
        return box;
    }

    /// <summary>Keeps a copy with that year's tax documents (a donation, a medical bill).</summary>
    public static void CopyToTaxes(Transaction t)
    {
        string path = FileVault.ReceiptPath(t.Receipt);
        if (!File.Exists(path)) { Toaster.Show("This receipt is on another device. It downloads when you sync."); return; }
        string folder = FileVault.TaxFolder(t.Date.Year);
        Directory.CreateDirectory(folder);
        string name = Guid.NewGuid().ToString("N") + Path.GetExtension(path).ToLowerInvariant();
        DataLock.WriteBytes(Path.Combine(folder, name), DataLock.ReadBytes(path));
        DataStore.TaxDocuments.Add(new TaxDocument
        {
            Year = t.Date.Year, Title = $"{(t.Payee.Length == 0 ? "Receipt" : t.Payee)} {t.Date:MMM d}", Kind = "Receipt",
            FileName = name, OriginalName = t.Receipt, Size = new FileInfo(path).Length,
        });
        Toaster.Show($"Copied to Taxes, {t.Date.Year}.");
    }
}
