using System.IO;
using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>A yes/no question in CoinPilot's style.</summary>
public sealed class ConfirmDialog : DialogShell
{
    public ConfirmDialog(string title, string message, string confirmText, bool danger = false)
        : base(title, message, allowDelete: false, saveText: confirmText, width: 480)
    {
        if (danger) UseDangerTone();
        else HeaderGlyph = "\uE9CE";
        if (danger)
            Loaded += (_, _) =>
            {
                foreach (var b in FindButtons(this).Where(b => (b.Content as string) == confirmText))
                    b.SetResourceReference(BackgroundProperty, "Brush.Danger");
            };
    }

    private static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button b) yield return b;
            foreach (var inner in FindButtons(child)) yield return inner;
        }
    }

    protected override string? TrySave() => null;

    public static bool Ask(string title, string message, string confirmText, bool danger = false) =>
        new ConfirmDialog(title, message, confirmText, danger).ShowDialog() == true;
}

/// <summary>Asks for one line of text (renaming a category, for example).</summary>
public sealed class TextPromptDialog : DialogShell
{
    private readonly TextBox _box;
    private readonly Func<string, string?> _validate;
    public string Value { get; private set; } = "";

    public TextPromptDialog(string title, string label, string value, string saveText, Func<string, string?> validate)
        : base(title, "", allowDelete: false, saveText: saveText, width: 460)
    {
        _validate = validate;
        _box = AddField(label, new TextBox { Text = value }, required: true);
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    protected override string? TrySave()
    {
        Value = _box.Text.Trim();
        return Value.Length == 0 ? "Enter a value." : _validate(Value);
    }
}

/// <summary>Turns on PIN/password protection, or changes the PIN/password.</summary>
public sealed class SetLockDialog : DialogShell
{
    private readonly bool _change;
    private readonly WrapPanel _kind;
    private readonly PasswordBox _first = new();
    private readonly PasswordBox _second = new();
    private readonly WrapPanel? _autoLock;

    /// <summary>The recovery key to show once (only when protection was just turned on).</summary>
    public string? RecoveryKey { get; private set; }

    public static readonly (int Minutes, string Label)[] AutoLockChoices =
        [(1, "1 minute"), (5, "5 minutes"), (15, "15 minutes"), (30, "30 minutes"), (60, "1 hour"), (0, "Never")];

    public SetLockDialog(bool change)
        : base(change ? "Change PIN or password" : "Protect your data",
               change ? "Choose a new PIN or password. Your recovery key keeps working."
                      : "Your data, receipts and tax documents will be encrypted. You'll need the PIN or password to open CoinPilot.",
               allowDelete: false, saveText: change ? "Change" : "Turn on protection", width: 540)
    {
        _change = change;
        _kind = AddField("Use a", Chips(["Password", "PIN"], DataLock.Info?.Kind ?? "Password"),
            hint: "A password is stronger. A PIN (4–12 digits) is quicker to type.");
        AddField(change ? "New PIN or password" : "PIN or password", _first, required: true);
        AddField("Type it again", _second, required: true);
        if (!change)
        {
            _autoLock = AddField("Lock automatically after", new WrapPanel());
            SetChips(_autoLock, AutoLockChoices.Select(c => (c.Minutes.ToString(), c.Label)), "15");
        }
        Loaded += (_, _) => _first.Focus();
    }

    protected override string? TrySave()
    {
        string kind = Selected(_kind) ?? "Password";
        string secret = _first.Password;
        if (DataLock.Validate(secret, kind) is string problem) return problem;
        if (secret != _second.Password) return "The two entries don't match.";

        Cursor = System.Windows.Input.Cursors.Wait;
        try
        {
            if (_change) DataLock.ChangeSecret(secret, kind);
            else RecoveryKey = DataLock.Enable(secret, kind, int.Parse(_autoLock is null ? "15" : Selected(_autoLock) ?? "15"));
        }
        catch (Exception ex)
        {
            return $"Couldn't {(_change ? "change it" : "turn on protection")}: {ex.Message}";
        }
        finally
        {
            Cursor = null;
        }
        return null;
    }
}

/// <summary>Shows the recovery key once, with copy / save options. Can't be closed until you confirm you've saved it.</summary>
public sealed class RecoveryKeyDialog : DialogShell
{
    private readonly CheckBox _saved;

    /// <summary>Lets the self-test close it without ticking the box.</summary>
    public bool ForceClose { get; set; }

    public RecoveryKeyDialog(string key)
        : base("Save your recovery key", "If you ever forget your PIN or password, this key is the only way to open your data. CoinPilot can't recover it for you.",
               allowDelete: false, saveText: "Done", width: 560)
    {
        HideCancel();
        var box = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(0, 0, 0, 12) };
        box.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        var text = new TextBox
        {
            Text = key, IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 20, FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        box.Child = text;
        Body.Children.Add(box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        var copy = new Button { Content = "Copy", Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 8, 0) };
        copy.Click += (_, _) => { Clipboard.SetText(key); Toaster.Show("Recovery key copied. Paste it somewhere safe."); };
        var save = new Button { Content = "Save to a file…", Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 8, 0) };
        save.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "CoinPilot recovery key.txt", Filter = "Text file (*.txt)|*.txt" };
            if (dlg.ShowDialog(this) == true)
                File.WriteAllText(dlg.FileName, $"CoinPilot recovery key\r\n\r\n{key}\r\n\r\nKeep this somewhere safe and separate from your computer (printed, or in a password manager).\r\n");
        };
        var print = new Button { Content = "Print", Style = (Style)FindResource("GhostButton") };
        print.Click += (_, _) =>
        {
            var pd = new PrintDialog();
            if (pd.ShowDialog() != true) return;
            var panel = new StackPanel { Margin = new Thickness(60) };
            panel.Children.Add(new TextBlock { Text = "CoinPilot recovery key", FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 16) });
            panel.Children.Add(new TextBlock { Text = key, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 24 });
            panel.Children.Add(new TextBlock { Text = $"Created {DateTime.Now:MMMM d, yyyy}. Keep this page somewhere safe.", Margin = new Thickness(0, 16, 0, 0) });
            panel.Measure(new Size(pd.PrintableAreaWidth, pd.PrintableAreaHeight));
            panel.Arrange(new Rect(new Size(pd.PrintableAreaWidth, pd.PrintableAreaHeight)));
            pd.PrintVisual(panel, "CoinPilot recovery key");
        };
        buttons.Children.Add(copy);
        buttons.Children.Add(save);
        buttons.Children.Add(print);
        Body.Children.Add(buttons);

        _saved = Check("I've saved my recovery key somewhere safe", false);
        Body.Children.Add(_saved);
        Closing += (_, e) => { if (DialogResult != true && !ForceClose) e.Cancel = _saved.IsChecked != true; };
    }

    protected override string? TrySave() => _saved.IsChecked == true ? null : "Save or copy the key, then tick the box to confirm.";
}

/// <summary>Asks for the current PIN/password (before turning protection off).</summary>
public sealed class VerifySecretDialog : DialogShell
{
    private readonly PasswordBox _box = new();

    public VerifySecretDialog(string title, string message, string saveText)
        : base(title, message, allowDelete: false, saveText: saveText, width: 480)
    {
        AddField(DataLock.Info?.Kind == "PIN" ? "Current PIN" : "Current password", _box, required: true);
        Loaded += (_, _) => _box.Focus();
    }

    protected override string? TrySave() => DataLock.Unlock(_box.Password) ? null : "That's not right.";
}

/// <summary>Edit spending and income categories (with subcategories) and the rules that pick categories automatically.</summary>
public sealed class CategoriesDialog : DialogShell
{
    private readonly StackPanel _spending = new();
    private readonly StackPanel _income = new();
    private readonly StackPanel _rules = new();
    private readonly TextBox _newName = new();
    private readonly WrapPanel _newKind;
    private readonly ComboBox _newParent = new();
    private readonly TextBox _ruleText = new();
    private readonly ComboBox _ruleCategory = new();

    public CategoriesDialog()
        : base("Categories and rules", "Add your own categories and subcategories, rename or hide them, and set rules that categorize payees automatically.",
               allowDelete: false, saveText: "Done", width: 700)
    {
        HideCancel();

        Body.Children.Add(Heading("Spending categories"));
        Body.Children.Add(_spending);
        Body.Children.Add(Heading("Income categories"));
        Body.Children.Add(_income);

        Body.Children.Add(Heading("Add a category"));
        var add = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        add.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        add.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        add.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        System.Windows.Automation.AutomationProperties.SetName(_newName, "New category name");
        add.Children.Add(_newName);
        _newParent.Margin = new Thickness(8, 0, 8, 0);
        System.Windows.Automation.AutomationProperties.SetName(_newParent, "Parent category");
        add.Children.Add(_newParent.At(1));
        var addButton = new Button { Content = "Add", Style = (Style)FindResource("PrimaryButton") };
        addButton.Click += (_, _) => AddCategory();
        add.Children.Add(addButton.At(2));
        _newKind = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        SetChips(_newKind, [CategoryKinds.Spending, CategoryKinds.Income], CategoryKinds.Spending);
        foreach (var chip in _newKind.Children.OfType<RadioButton>()) chip.Checked += (_, _) => FillParents();
        Body.Children.Add(_newKind);
        Body.Children.Add(add);
        Body.Children.Add(new TextBlock { Text = "Pick a parent to make it a subcategory (like Dining out › Coffee). Budgets and reports for the parent include it.", Style = (Style)FindResource("Muted"), FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });

        Body.Children.Add(Heading("Rules"));
        Body.Children.Add(new TextBlock { Text = "When a payee contains the text, CoinPilot picks the category for new and imported transactions.", Style = (Style)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 8) });
        Body.Children.Add(_rules);
        var ruleRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        ruleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ruleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ruleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        System.Windows.Automation.AutomationProperties.SetName(_ruleText, "Payee contains");
        ruleRow.Children.Add(_ruleText);
        _ruleCategory.Margin = new Thickness(8, 0, 8, 0);
        System.Windows.Automation.AutomationProperties.SetName(_ruleCategory, "Rule category");
        ruleRow.Children.Add(_ruleCategory.At(1));
        var addRule = new Button { Content = "Add rule", Style = (Style)FindResource("PrimaryButton") };
        addRule.Click += (_, _) => AddRule();
        ruleRow.Children.Add(addRule.At(2));
        Body.Children.Add(ruleRow);

        Refresh();
    }

    private TextBlock Heading(string text) => new() { Text = text, Style = (Style)FindResource("CardTitle"), Margin = new Thickness(0, 14, 0, 8) };

    private void Refresh()
    {
        Fill(_spending, CategoryKinds.Spending);
        Fill(_income, CategoryKinds.Income);
        FillParents();

        _ruleCategory.Items.Clear();
        foreach (var c in Categories.Spending.Concat(Categories.Income))
            _ruleCategory.Items.Add(new ComboBoxItem { Content = Categories.Display(c), Tag = c });
        if (_ruleCategory.Items.Count > 0) _ruleCategory.SelectedIndex = 0;

        _rules.Children.Clear();
        if (DataStore.Rules.Items.Count == 0)
            _rules.Children.Add(Ui.Text("No rules yet. You can also create one from the add-transaction window.", muted: true, size: 12.5));
        foreach (var r in DataStore.Rules.Items.OrderBy(r => r.Contains))
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var delete = new Button { Content = "Remove", Style = (Style)FindResource("LinkButton") };
            delete.Click += (_, _) => { DataStore.Rules.Delete(r); Refresh(); };
            DockPanel.SetDock(delete, Dock.Right);
            row.Children.Add(delete);
            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            text.Inlines.Add("Payee contains ");
            text.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run($"\"{r.Contains}\"")));
            text.Inlines.Add(" → ");
            text.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run(Categories.Display(r.Category))));
            row.Children.Add(text);
            _rules.Children.Add(row);
        }
    }

    private void Fill(StackPanel panel, string kind)
    {
        panel.Children.Clear();
        var wrap = new WrapPanel();
        foreach (var c in Categories.AllDefs(kind))
        {
            bool sub = c.Parent.Length > 0;
            var chip = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 6, 5), Margin = new Thickness(0, 0, 8, 8), BorderThickness = new Thickness(1) };
            chip.SetResourceReference(Border.BackgroundProperty, c.IsHidden ? "Brush.Bg" : "Brush.Surface");
            chip.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Ui.Text(sub ? $"{c.Parent} › {c.Name}" : c.Name, size: 13, muted: c.IsHidden));
            if (c.IsHidden) row.Children.Add(Ui.Text("  (hidden)", muted: true, size: 12));

            var menu = new Button { Content = "", Style = (Style)FindResource("BaseButton"), Padding = new Thickness(6, 2, 6, 2), MinHeight = 22, FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"), FontSize = 11, Margin = new Thickness(4, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(menu, $"Options for {c.Name}");
            var context = new ContextMenu();
            var name = c.Name;
            var rename = new MenuItem { Header = "Rename…" };
            rename.Click += (_, _) => Rename(name);
            var hide = new MenuItem { Header = c.IsHidden ? "Show" : "Hide" };
            hide.Click += (_, _) => ToggleHidden(name);
            var delete = new MenuItem { Header = "Delete", IsEnabled = !Categories.IsUsed(name) && !Categories.Children(name).Any() };
            delete.Click += (_, _) => Delete(name);
            if (!delete.IsEnabled) delete.ToolTip = "In use. Hide it instead, or rename it.";
            ToolTipService.SetShowOnDisabled(delete, true);
            context.Items.Add(rename);
            context.Items.Add(hide);
            context.Items.Add(delete);
            menu.ContextMenu = context;
            menu.Click += (_, _) => { context.PlacementTarget = menu; context.IsOpen = true; };
            row.Children.Add(menu);
            chip.Child = row;
            wrap.Children.Add(chip);
        }
        panel.Children.Add(wrap);
    }

    private void FillParents()
    {
        string kind = Selected(_newKind) ?? CategoryKinds.Spending;
        _newParent.Items.Clear();
        _newParent.Items.Add(new ComboBoxItem { Content = "No parent (top level)", Tag = "" });
        foreach (var c in Categories.AllDefs(kind).Where(c => c.Parent.Length == 0 && !c.IsHidden))
            _newParent.Items.Add(new ComboBoxItem { Content = "Under " + c.Name, Tag = c.Name });
        _newParent.SelectedIndex = 0;
    }

    private static bool Exists(string name) =>
        Categories.AllDefs(CategoryKinds.Spending).Concat(Categories.AllDefs(CategoryKinds.Income))
            .Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    private void AddCategory()
    {
        string name = _newName.Text.Trim();
        if (name.Length == 0) return;
        if (name.Contains('›') || name.Equals("Split", StringComparison.OrdinalIgnoreCase) || name.Equals("Transfer", StringComparison.OrdinalIgnoreCase))
        {
            Toaster.Show("Pick a different name for the category.");
            return;
        }
        if (Exists(name))
        {
            Toaster.Show($"There's already a category called \"{name}\".");
            return;
        }
        Categories.Add(name, Selected(_newKind) ?? CategoryKinds.Spending, (_newParent.SelectedItem as ComboBoxItem)?.Tag as string ?? "");
        _newName.Clear();
        Refresh();
    }

    private void Rename(string name)
    {
        var prompt = new TextPromptDialog("Rename category", "New name", name, "Rename",
            v => !string.Equals(v, name, StringComparison.OrdinalIgnoreCase) && Exists(v) ? $"There's already a category called \"{v}\"." : null) { Owner = this };
        if (prompt.ShowDialog() != true) return;
        Categories.Rename(name, prompt.Value);
        Refresh();
    }

    private void ToggleHidden(string name)
    {
        Categories.EnsureSaved();
        var def = DataStore.CategoryDefs.Items.First(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        def.IsHidden = !def.IsHidden;
        DataStore.CategoryDefs.Update(def);
        Refresh();
    }

    private void Delete(string name)
    {
        Categories.EnsureSaved();
        var def = DataStore.CategoryDefs.Items.First(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        DataStore.CategoryDefs.Delete(def);
        DataStore.Rules.Items.Where(r => string.Equals(r.Category, name, StringComparison.OrdinalIgnoreCase)).ToList().ForEach(DataStore.Rules.Delete);
        Refresh();
    }

    private void AddRule()
    {
        string text = _ruleText.Text.Trim();
        if (text.Length < 2) { Toaster.Show("Enter at least 2 characters of the payee."); return; }
        if ((_ruleCategory.SelectedItem as ComboBoxItem)?.Tag is not string category) return;
        DataStore.Rules.Add(new CategoryRule { Contains = text, Category = category });
        _ruleText.Clear();
        Refresh();
    }

    protected override string? TrySave() => null;
}
