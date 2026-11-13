using System.IO;
using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>
/// The Taxes page: a safe place for tax documents (W-2s, 1099s, returns, receipts), organized by tax year.
/// Files are copied into CoinPilot's data folder, encrypted when PIN/password protection is on, and included in backups.
/// </summary>
public sealed class TaxesView : DataPage
{
    /// <summary>The tax year being viewed. Defaults to last year (the one you file for this spring).</summary>
    public static int Year { get; set; } = DateTime.Today.Year - 1;

    private static readonly (string Kind, string Label)[] Checklist =
    [
        ("W-2", "W-2 (wages)"), ("1099", "1099 (other income)"), ("1098", "1098 (mortgage / student loan interest)"), ("Tax return", "Your filed return"),
    ];

    public TaxesView()
    {
        AllowDrop = true;
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                foreach (var f in files.Where(File.Exists)) TaxDocumentDialog.Add(Year, f);
        };
    }

    protected override void Build()
    {
        var docs = DataStore.TaxDocuments.Items;
        var years = docs.Select(d => d.Year).Append(DateTime.Today.Year).Append(DateTime.Today.Year - 1).Append(Year)
            .Distinct().OrderByDescending(y => y).ToList();
        var forYear = docs.Where(d => d.Year == Year).ToList();

        Add(PageKit.Header("", "Taxes", "Keep your tax documents safe, organized by year."));
        Add(PageKit.ActionBar(DataLock.IsEnabled
                ? "Documents are copied into CoinPilot, encrypted with your PIN/password, and included in backups."
                : "Documents are copied into CoinPilot and included in backups. Add a PIN or password in Settings to encrypt them.",
            PageKit.Primary("", "Add document", (_, _) => TaxDocumentDialog.Add(Year, null))));

        var yearBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        yearBar.Children.Add(Ui.Text("Tax year", muted: true).Also(t => t.Margin = new Thickness(0, 0, 10, 0)));
        yearBar.Children.Add(PageKit.ChoiceChips(years.Select(y => y.ToString()), Year.ToString(), pick => { Year = int.Parse(pick); Rebuild(); }));
        Add(yearBar);

        decimal taxesPaid = DataStore.Transactions.Items
            .Where(t => t.Date.Year == Year && t.IsSpending)
            .SelectMany(t => t.Parts())
            .Where(p => string.Equals(Categories.Root(p.Category), "Taxes", StringComparison.OrdinalIgnoreCase))
            .Sum(p => -p.Amount);
        int checklistDone = Checklist.Count(c => forYear.Any(d => d.Kind == c.Kind));
        Add(PageKit.KpiRow(
            PageKit.Kpi($"{Year} documents", forYear.Count.ToString(), forYear.Count == 0 ? "None added yet" : FileVault.SizeText(forYear.Sum(d => d.Size)), hero: true),
            PageKit.Kpi("Checklist", $"{checklistDone} of {Checklist.Length}", "Common documents"),
            PageKit.Kpi($"Taxes paid in {Year}", Money.Format(taxesPaid), "Transactions in the Taxes category"),
            PageKit.Kpi("All years", docs.Count.ToString(), docs.Count == 1 ? "document" : "documents")));

        // Checklist
        var checks = new WrapPanel();
        foreach (var (kind, label) in Checklist)
        {
            bool have = forYear.Any(d => d.Kind == kind);
            var item = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 14, 8), Margin = new Thickness(0, 0, 10, 10) };
            item.SetResourceReference(Border.BackgroundProperty, have ? "Brush.AccentSoft" : "Brush.SurfaceAlt");
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new TextBlock { Text = have ? "" : "", Style = Res("Icon"), FontSize = 12, Margin = new Thickness(0, 0, 8, 0) };
            icon.SetResourceReference(TextBlock.ForegroundProperty, have ? "Brush.Accent" : "Brush.TextMuted");
            row.Children.Add(icon);
            row.Children.Add(Ui.Text(label, size: 13, weight: have ? FontWeights.SemiBold : FontWeights.Normal, brushKey: have ? "Brush.Text" : "Brush.TextMuted"));
            item.Child = row;
            if (!have)
            {
                item.Cursor = System.Windows.Input.Cursors.Hand;
                item.ToolTip = $"Add your {label}";
                string k = kind;
                item.MouseLeftButtonUp += (_, _) => TaxDocumentDialog.Add(Year, null, k);
            }
            checks.Children.Add(item);
        }
        Add(PageKit.Card(checks, $"{Year} checklist", padding: new Thickness(20, 18, 20, 10)));

        if (forYear.Count == 0)
        {
            Add(PageKit.Card(Ui.EmptyState("", $"No documents for {Year}",
                "Add PDFs or photos of your W-2s, 1099s, your filed return and receipts. You can also drag files onto this page.",
                "Add document", (_, _) => TaxDocumentDialog.Add(Year, null))));
            Add(Note());
            return;
        }

        var list = new StackPanel();
        bool first = true;
        foreach (var group in forYear.GroupBy(d => d.Kind).OrderBy(g => Array.IndexOf(TaxDocumentKinds.All, g.Key) is var i && i < 0 ? 99 : i))
        {
            list.Children.Add(PageKit.Section(group.Key, first ? 8 : 16));
            foreach (var d in group.OrderBy(d => d.Title))
            {
                list.Children.Add(BuildRow(d, first));
                first = false;
            }
        }
        Add(PageKit.Card(list, padding: new Thickness(14, 6, 14, 8)));
        Add(Note());
    }

    private static TextBlock Note() => new()
    {
        Text = "Tip: drag files from File Explorer onto this page to add them. CoinPilot keeps your documents; it doesn't file taxes.",
        FontSize = 12,
        Margin = new Thickness(2, 0, 0, 0),
        Style = (Style)Application.Current.FindResource("Muted"),
    };

    private Button BuildRow(TaxDocument d, bool firstRow)
    {
        var grid = new Grid { Margin = new Thickness(10, 10, 10, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        string ext = Path.GetExtension(d.FileName).ToLowerInvariant();
        var tile = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 14, 0) };
        tile.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        var glyph = new TextBlock
        {
            Text = ext is ".jpg" or ".jpeg" or ".png" or ".heic" or ".gif" ? "" : "",
            Style = Res("Icon"), FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center,
        };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        tile.Child = glyph;
        grid.Children.Add(tile);

        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center }.At(1);
        middle.Children.Add(Ui.Text(d.Title, weight: FontWeights.SemiBold, size: 14));
        var parts = new List<string> { ext.TrimStart('.').ToUpperInvariant(), FileVault.SizeText(d.Size), $"added {d.CreatedAt:MMM d, yyyy}" };
        if (d.Note.Length > 0) parts.Add(d.Note);
        middle.Children.Add(Ui.Text(string.Join(" · ", parts), muted: true, size: 12));
        grid.Children.Add(middle);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }.At(2);
        buttons.Children.Add(PageKit.Link("Open", () => TaxDocumentDialog.Open(d)));
        buttons.Children.Add(PageKit.Link("Save a copy", () => TaxDocumentDialog.SaveCopy(d)));
        grid.Children.Add(buttons);

        return PageKit.Row(grid, $"{d.Title}, {d.Kind}, {d.Year}", () => TaxDocumentDialog.Edit(d), firstRow);
    }
}

/// <summary>Add a tax document (pick the file, name it, choose its type and year) or edit one.</summary>
public sealed class TaxDocumentDialog : DialogShell
{
    private readonly TaxDocument _target;
    private readonly bool _isNew;
    private readonly TextBox _title;
    private readonly WrapPanel _kind;
    private readonly WrapPanel _year;
    private readonly TextBox _note;
    private readonly StackPanel _filePanel = new() { Orientation = Orientation.Horizontal };
    private string? _source;

    public TaxDocument Result => _target;

    public TaxDocumentDialog(TaxDocument? existing, int year, string? sourceFile = null, string? kind = null)
        : base(existing == null ? "Add tax document" : "Edit tax document",
               existing == null ? "A copy of the file is kept in CoinPilot, so it's safe even if you delete the original." : "Rename it, change its type or year, or delete it.",
               allowDelete: existing != null, saveText: existing == null ? "Add document" : "Save")
    {
        _isNew = existing == null;
        _target = existing ?? new TaxDocument { Year = year, Kind = kind ?? TaxDocumentKinds.Other };
        _source = sourceFile;

        if (_isNew)
        {
            AddField("File", _filePanel, required: true, hint: "PDF, photo or any document.");
            BuildFileRow();
        }
        _title = AddField("Name", new TextBox { Text = _target.Title.Length > 0 ? _target.Title : sourceFile != null ? Path.GetFileNameWithoutExtension(sourceFile) : "" },
            hint: "Like \"W-2 from Acme Corp\" or \"2025 federal return\".", required: true);
        _kind = AddField("Type", Chips(TaxDocumentKinds.All, _target.Kind));
        var years = Enumerable.Range(0, 8).Select(i => (DateTime.Today.Year - i).ToString()).ToList();
        if (!years.Contains(_target.Year.ToString())) years.Add(_target.Year.ToString());
        _year = AddField("Tax year", Chips(years, _target.Year.ToString()));
        _note = AddField("Note", new TextBox { Text = _target.Note });

        // Picking a type fills in a sensible name for an untitled document.
        foreach (var chip in _kind.Children.OfType<RadioButton>())
            chip.Checked += (_, _) =>
            {
                if (_isNew && (_title.Text.Length == 0 || (_source != null && _title.Text == Path.GetFileNameWithoutExtension(_source))))
                    _title.Text = $"{Selected(_kind)} {Selected(_year)}";
            };
        Loaded += (_, _) => _title.Focus();
    }

    private void BuildFileRow()
    {
        _filePanel.Children.Clear();
        if (_source != null)
        {
            var icon = new TextBlock { Text = "", Style = (Style)FindResource("Icon"), Margin = new Thickness(0, 0, 8, 0) };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            _filePanel.Children.Add(icon);
            _filePanel.Children.Add(Ui.Text($"{Path.GetFileName(_source)} ({FileVault.SizeText(new FileInfo(_source).Length)})").Also(t => t.Margin = new Thickness(0, 0, 12, 0)));
        }
        var choose = new Button { Content = _source == null ? "Choose a file…" : "Change…", Style = (Style)FindResource(_source == null ? "GhostButton" : "LinkButton") };
        choose.Click += (_, _) =>
        {
            var pick = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a tax document",
                Filter = "Documents and images (*.pdf;*.jpg;*.jpeg;*.png;*.heic;*.docx;*.xlsx;*.txt)|*.pdf;*.jpg;*.jpeg;*.png;*.heic;*.docx;*.xlsx;*.txt|All files (*.*)|*.*",
            };
            if (pick.ShowDialog(this) != true) return;
            bool untitled = _title.Text.Length == 0 || (_source != null && _title.Text == Path.GetFileNameWithoutExtension(_source));
            _source = pick.FileName;
            if (untitled) _title.Text = Path.GetFileNameWithoutExtension(_source);
            BuildFileRow();
        };
        _filePanel.Children.Add(choose);
    }

    protected override string? TrySave()
    {
        if (_isNew && (_source == null || !File.Exists(_source))) return "Choose the file to add.";
        string title = _title.Text.Trim();
        if (title.Length == 0) return "Give the document a name.";
        int year = int.Parse(Selected(_year) ?? _target.Year.ToString());

        try
        {
            if (_isNew)
            {
                _target.FileName = FileVault.StoreTaxDocument(year, _source!);
                _target.OriginalName = Path.GetFileName(_source!);
                _target.Size = new FileInfo(_source!).Length;
                _target.Year = year;
            }
            else FileVault.MoveTaxDocument(_target, year);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't save the file: {ex.Message}";
        }

        _target.Title = title;
        _target.Kind = Selected(_kind) ?? TaxDocumentKinds.Other;
        _target.Note = _note.Text.Trim();
        return null;
    }

    public static bool Add(int year, string? file, string? kind = null)
    {
        var dialog = new TaxDocumentDialog(null, year, file, kind);
        if (dialog.ShowDialog() != true) return false;
        DataStore.TaxDocuments.Add(dialog.Result);
        TaxesView.Year = dialog.Result.Year;
        Toaster.Show($"Added {dialog.Result.Title} to {dialog.Result.Year}.");
        return true;
    }

    public static bool Edit(TaxDocument d)
    {
        var dialog = new TaxDocumentDialog(d, d.Year);
        if (dialog.ShowDialog() != true) return false;
        if (dialog.DeleteRequested)
        {
            FileVault.Delete(FileVault.TaxDocumentPath(d));
            DataStore.TaxDocuments.Delete(d);
            Toaster.Show($"Deleted {d.Title}.");
        }
        else
        {
            DataStore.TaxDocuments.Update(d);
            Toaster.Show("Document saved.");
        }
        return true;
    }

    public static void Open(TaxDocument d)
    {
        try { FileVault.Open(FileVault.TaxDocumentPath(d), d.Title + Path.GetExtension(d.FileName)); }
        catch (Exception ex) { Toaster.Show($"Couldn't open it: {ex.Message}"); }
    }

    public static void SaveCopy(TaxDocument d)
    {
        var save = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save a copy",
            FileName = d.OriginalName.Length > 0 ? d.OriginalName : d.Title + Path.GetExtension(d.FileName),
            Filter = "All files (*.*)|*.*",
        };
        if (save.ShowDialog() != true) return;
        try
        {
            FileVault.SaveCopy(FileVault.TaxDocumentPath(d), save.FileName);
            Toaster.Show($"Saved a copy to {Path.GetFileName(save.FileName)}.");
        }
        catch (Exception ex) { Toaster.Show($"Couldn't save the copy: {ex.Message}"); }
    }
}
