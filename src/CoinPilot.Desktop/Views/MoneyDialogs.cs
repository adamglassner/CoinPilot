using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

// Add/edit dialogs for Accounts, Debts, Bills and Goals, plus the "record a payment" / "add money" dialogs.
// Each has a static Edit(...) that opens it and saves the result.

/// <summary>Add or edit a bank account, card or wallet.</summary>
public sealed class AccountDialog : DialogShell
{
    private readonly Account _target;
    private readonly TextBox _name;
    private readonly WrapPanel _type;
    private readonly TextBox _institution;
    private readonly TextBox _balance;
    private readonly DatePicker _date;
    private readonly CheckBox? _closed;
    private readonly Account? _existing;

    public Account Result => _target;

    public AccountDialog(Account? existing, string? name = null)
        : base(existing == null ? "Add account" : "Edit account",
               existing == null ? "Add a bank account, credit card or cash so WillowWallet can track its balance."
                                : "Change the details, or delete it. Its transactions are kept.",
               allowDelete: existing != null, saveText: existing == null ? "Add account" : "Save")
    {
        _existing = existing;
        _target = existing ?? new Account { Name = name ?? "" };
        if (existing == null && name != null)
            _target.Type = AccountTypes.All.FirstOrDefault(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase)) ?? AccountTypes.Checking;

        _name = AddField("Name", new TextBox { Text = _target.Name }, hint: "Like \"Chase checking\", \"Visa\" or \"Venmo\". Transactions pick accounts by this name.", required: true);
        _type = AddField("Type", Chips(AccountTypes.All, _target.Type));
        _institution = AddField("Bank or company", new TextBox { Text = _target.Institution });
        if (existing == null)
        {
            // One click for the common payment apps
            var apps = new WrapPanel();
            TextBox? balanceLater = null;
            Loaded += (_, _) => balanceLater = _balance;
            foreach (var (app, company) in AccountTypes.PaymentApps.Where(p => !DataStore.Accounts.Items.Any(a => string.Equals(a.Name, p.Name, StringComparison.OrdinalIgnoreCase))))
            {
                var b = new Button { Content = app, Style = (System.Windows.Style)FindResource("GhostButton"), Margin = new System.Windows.Thickness(0, 0, 8, 8), Padding = new System.Windows.Thickness(14, 6, 14, 6), MinHeight = 32 };
                System.Windows.Automation.AutomationProperties.SetName(b, $"Add {app}");
                b.Click += (_, _) =>
                {
                    _name.Text = app;
                    _institution.Text = company;
                    foreach (var chip in _type.Children.OfType<RadioButton>())
                        chip.IsChecked = (chip.Tag as string ?? chip.Content as string) == AccountTypes.PaymentApp;
                    balanceLater?.Focus();
                };
                apps.Children.Add(b);
            }
            if (apps.Children.Count > 0)
                AddField("Payment apps", apps, hint: "Fills in the name and type. Then enter the balance you have in the app.");
        }
        _balance = AddField("Balance", AmountBox(existing == null ? null : Math.Abs(existing.StartingBalance)),
            hint: "What's in the account on the date below. For a credit card, enter what you owe. Transactions after that date are added automatically.", required: true);
        _date = AddField("Balance as of", DateBox(_target.BalanceDate), required: true);
        if (existing != null) _closed = AddField("Status", Check("This account is closed (hide it from lists and new transactions)", existing.IsClosed));

        Loaded += (_, _) => _name.Focus();
    }

    protected override string? TrySave()
    {
        string name = _name.Text.Trim();
        if (name.Length == 0) return "Enter a name for the account.";
        if (DataStore.Accounts.Items.Any(a => a != _existing && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            return $"You already have an account called \"{name}\".";
        if (!ReadAmount(_balance, allowZero: true, out var balance)) return "Enter the balance as a number, like 1250.00 (or 0).";
        if (_date.SelectedDate is not DateTime date) return "Pick the date of that balance.";

        _target.Name = name;
        _target.Type = Selected(_type) ?? AccountTypes.Checking;
        _target.Institution = _institution.Text.Trim();
        _target.StartingBalance = _target.IsLiability ? -balance : balance;
        _target.BalanceDate = date.Date;
        _target.IsClosed = _closed?.IsChecked == true;
        return null;
    }

    public static bool Edit(Account? existing, string? name = null)
    {
        string oldName = existing?.Name ?? "";
        var dialog = new AccountDialog(existing, name);
        if (dialog.ShowDialog() != true) return false;

        if (existing == null)
        {
            DataStore.Accounts.Add(dialog.Result);
            Toaster.Show($"Added {dialog.Result.Name}.");
        }
        else if (dialog.DeleteRequested)
        {
            DataStore.Accounts.Delete(existing);
            Toaster.Show($"Deleted {existing.Name}. Its transactions are kept.");
        }
        else
        {
            DataStore.Accounts.Update(existing);
            AccountMath.Rename(oldName, existing.Name);
            Toaster.Show("Account saved.");
        }
        return true;
    }
}

/// <summary>Add or edit a debt: what's owed, its interest rate and minimum payment.</summary>
public sealed class DebtDialog : DialogShell
{
    private readonly Debt _target;
    private readonly TextBox _name;
    private readonly WrapPanel _type;
    private readonly TextBox _balance;
    private readonly TextBox _rate;
    private readonly TextBox _minimum;
    private readonly TextBox _note;
    private readonly CheckBox? _makeBill;
    private readonly DatePicker? _billDue;
    private readonly WrapPanel? _billAccount;
    private readonly decimal _oldMinimum;

    public Debt Result => _target;
    /// <summary>Set up a recurring monthly bill for the payment (first due date and account) after saving.</summary>
    public (DateTime FirstDue, string Account)? NewBill { get; private set; }

    public DebtDialog(Debt? existing)
        : base(existing == null ? "Add debt" : "Edit debt",
               existing == null ? "Add a loan or card balance you're paying down." : "Update the details, or delete it.",
               allowDelete: existing != null, saveText: existing == null ? "Add debt" : "Save")
    {
        _target = existing ?? new Debt();
        _name = AddField("Name", new TextBox { Text = _target.Name }, hint: "Like \"Visa card\" or \"Car loan\".", required: true);
        _type = AddField("Type", Chips(DebtTypes.All, _target.Type));
        _balance = AddField(existing == null ? "Amount owed" : "Amount owed now", AmountBox(existing?.Balance), required: true);
        _rate = AddField("Interest rate (APR %)", SmallBox(existing == null || existing.InterestRate == 0 ? "" : existing.InterestRate.ToString("0.###", Money.Us)),
            hint: "The yearly rate from your statement, like 22.99. Leave empty for 0%.");
        _minimum = AddField("Minimum monthly payment", AmountBox(existing?.MinimumPayment), required: true);
        _oldMinimum = existing?.MinimumPayment ?? 0;

        // The monthly payment as a recurring bill (due dates, reminders, Mark paid lowers the balance)
        var bill = existing == null ? null : DebtLink.BillFor(existing);
        if (bill != null)
        {
            var info = new TextBlock { TextWrapping = System.Windows.TextWrapping.Wrap, LineHeight = 19 };
            info.Text = bill.IsDone
                ? $"Paid by the bill \"{bill.Name}\", which ended when the debt was paid off."
                : $"Paid by the bill \"{bill.Name}\": {Money.Format(bill.Amount)} {bill.Repeats.ToLowerInvariant()}, next due {bill.NextDue:MMM d}. Marking it paid on the Bills page lowers this balance.";
            AddField("Monthly bill", info);
        }
        else if (existing == null || !existing.IsPaidOff)
        {
            _makeBill = AddField("Monthly bill", Check("Add the payment as a recurring monthly bill", existing == null),
                hint: "It shows on the Bills page with its due date and reminders. Marking it paid lowers this balance, and the bill ends when the debt is paid off.");
            var firstDue = DateTime.Today.AddMonths(1);
            _billDue = AddField("Payment due", DateBox(firstDue), hint: "The next due date. It repeats on this day every month.");
            var accounts = DataStore.AccountChoices();
            _billAccount = AddField("Paid from", Chips(accounts, accounts[0]));
            void Toggle() { ShowField(_billDue, _makeBill.IsChecked == true); ShowField(_billAccount, _makeBill.IsChecked == true); }
            _makeBill.Checked += (_, _) => Toggle();
            _makeBill.Unchecked += (_, _) => Toggle();
            Toggle();
        }
        _note = AddField("Note", new TextBox { Text = _target.Note });
        Loaded += (_, _) => _name.Focus();
    }

    protected override string? TrySave()
    {
        string name = _name.Text.Trim();
        if (name.Length == 0) return "Enter a name for the debt.";
        if (!ReadAmount(_balance, allowZero: _target.Id.Length > 0, out var balance)) return "Enter how much you owe, like 4200.00.";
        if (!Money.TryParsePercent(_rate.Text, out var rate)) return "Enter the interest rate as a percent between 0 and 100, like 22.99.";
        if (!ReadAmount(_minimum, allowZero: true, out var minimum)) return "Enter the minimum monthly payment, like 95.00 (or 0).";

        _target.Name = name;
        _target.Type = Selected(_type) ?? DebtTypes.CreditCard;
        _target.Balance = balance;
        if (_target.Id.Length == 0 || balance > _target.OriginalBalance) _target.OriginalBalance = balance;
        _target.InterestRate = rate;
        _target.MinimumPayment = minimum;
        _target.Note = _note.Text.Trim();
        NewBill = null;
        if (_makeBill?.IsChecked == true)
        {
            if (minimum <= 0) return "Enter the monthly payment so the bill knows how much it is.";
            if (_billDue!.SelectedDate is not DateTime due) return "Pick when the payment is due.";
            NewBill = (due.Date, Selected(_billAccount!) ?? DataStore.AccountChoices()[0]);
        }
        return null;
    }

    public static bool Edit(Debt? existing)
    {
        var dialog = new DebtDialog(existing);
        if (dialog.ShowDialog() != true) return false;
        var d = dialog.Result;
        if (dialog.DeleteRequested && existing != null)
        {
            int unlinked = DebtLink.Unlink(existing);
            DataStore.Debts.Delete(existing);
            Toaster.Show(unlinked > 0 ? $"Deleted {existing.Name}. Its bill stays on the Bills page as a regular bill." : $"Deleted {existing.Name}.");
            return true;
        }
        if (existing == null) DataStore.Debts.Add(d);
        else
        {
            DataStore.Debts.Update(existing);
            // The bill follows a changed minimum payment if it was paying the old minimum
            if (DebtLink.BillFor(existing) is { IsDone: false } linked && linked.Amount == dialog._oldMinimum && d.MinimumPayment > 0 && d.MinimumPayment != linked.Amount)
            {
                linked.Amount = d.MinimumPayment;
                DataStore.Bills.Update(linked);
            }
        }
        if (dialog.NewBill is { } nb)
        {
            var bill = DebtLink.CreateBill(d, nb.FirstDue, nb.Account);
            Toaster.Show($"{(existing == null ? "Added" : "Saved")} {d.Name}, with a {Money.Format(bill.Amount)} monthly bill due {bill.NextDue:MMM d}.");
        }
        else Toaster.Show(existing == null ? $"Added {d.Name}." : "Debt saved.");
        return true;
    }
}

/// <summary>Record a payment toward a debt. Lowers the balance and (optionally) adds it to Transactions.</summary>
public sealed class DebtPaymentDialog : DialogShell
{
    private readonly Debt _debt;
    private readonly TextBox _amount;
    private readonly DatePicker _date;
    private readonly WrapPanel _account;
    private readonly CheckBox _addTransaction;
    private readonly CheckBox? _countsForBill;
    private readonly Bill? _bill;

    public decimal Amount { get; private set; }
    /// <summary>The payment is the debt's bill for its current due date (moves the bill on).</summary>
    public bool PaysBill { get; private set; }
    public DateTime Date { get; private set; }
    public string Account { get; private set; } = "";
    public bool AddTransaction { get; private set; }

    public DebtPaymentDialog(Debt debt)
        : base($"Pay {debt.Name}", $"{Money.Format(debt.Balance)} owed. The payment lowers the balance.", allowDelete: false, saveText: "Record payment")
    {
        _debt = debt;
        decimal suggested = debt.MinimumPayment > 0 ? Math.Min(debt.MinimumPayment, debt.Balance) : debt.Balance;
        _amount = AddField("Payment", AmountBox(suggested), required: true);
        _date = AddField("Date", DateBox(DateTime.Today), required: true);
        var accounts = DataStore.AccountChoices();
        _account = AddField("Paid from", Chips(accounts, accounts[0]));
        _addTransaction = AddField("Transactions", Check($"Also add it to Transactions (category \"{Categories.DebtPayments}\")", true));
        _bill = DebtLink.BillFor(debt) is { IsDone: false } b ? b : null;
        if (_bill != null)
            _countsForBill = AddField("Bill", Check($"This is the \"{_bill.Name}\" payment due {_bill.NextDue:MMM d} (moves the bill to its next due date)",
                DebtLink.LooksLikeBillPayment(_bill, DateTime.Today)));
        Loaded += (_, _) => { _amount.Focus(); _amount.SelectAll(); };
    }

    protected override string? TrySave()
    {
        if (!ReadAmount(_amount, allowZero: false, out var amount)) return "Enter the payment amount, like 150.00.";
        if (_date.SelectedDate is not DateTime date) return "Pick the date of the payment.";
        Amount = amount;
        Date = date.Date;
        Account = Selected(_account) ?? DataStore.AccountChoices()[0];
        AddTransaction = _addTransaction.IsChecked == true;
        PaysBill = _countsForBill?.IsChecked == true;
        return null;
    }

    public static bool Record(Debt debt)
    {
        var dialog = new DebtPaymentDialog(debt);
        if (dialog.ShowDialog() != true) return false;
        Apply(debt, dialog.Amount, dialog.Date, dialog.Account, dialog.AddTransaction);
        var bill = DebtLink.BillFor(debt);
        if (bill is { IsDone: false } && (dialog.PaysBill || debt.IsPaidOff)) DebtLink.Advance(bill, dialog.Date, debt);
        Toaster.Show(debt.IsPaidOff
            ? $"{debt.Name} is paid off!{(bill != null ? " Its monthly bill has ended." : "")}"
            : $"Paid {Money.Format(dialog.Amount)} toward {debt.Name}. {Money.Format(debt.Balance)} left.{(dialog.PaysBill && bill != null ? $" Next bill due {bill.NextDue:MMM d}." : "")}");
        return true;
    }

    /// <summary>Lowers the balance, keeps the payment history and (optionally) records the transaction.</summary>
    public static void Apply(Debt debt, decimal amount, DateTime date, string account, bool addTransaction) =>
        DebtLink.ApplyPayment(debt, amount, date, account, addTransaction);
}

/// <summary>Add or edit a bill or subscription.</summary>
public sealed class BillDialog : DialogShell
{
    private readonly Bill _target;
    private readonly TextBox _name;
    private readonly TextBox _amount;
    private readonly DatePicker _due;
    private readonly WrapPanel _repeats;
    private readonly WrapPanel _category;
    private readonly WrapPanel _account;
    private readonly CheckBox _autoPay;
    private readonly TextBox _note;
    private readonly WrapPanel? _debt;

    public Bill Result => _target;

    public BillDialog(Bill? existing, Debt? forDebt = null)
        : base(existing == null ? "Add bill" : "Edit bill",
               existing == null ? "Add a bill or subscription so you know what's coming up." : "Change the details, or delete it.",
               allowDelete: existing != null, saveText: existing == null ? "Add bill" : "Save")
    {
        _target = existing ?? new Bill { NextDue = DateTime.Today.AddDays(7), Account = DataStore.AccountChoices()[0] };
        if (existing == null && forDebt != null)
        {
            _target.Name = forDebt.Name;
            _target.DebtId = forDebt.Id;
            _target.Category = Categories.DebtPayments;
            _target.Amount = forDebt.MinimumPayment;
        }
        _name = AddField("Name", new TextBox { Text = _target.Name }, hint: "Like \"Electric\", \"Car insurance\", \"Netflix\" or \"Student loan\".", required: true);
        _amount = AddField("Amount", AmountBox(_target.Amount > 0 ? _target.Amount : null), hint: "If it changes each time, enter a typical amount.", required: true);
        _due = AddField(existing == null ? "First due date" : "Next due date", DateBox(_target.NextDue), required: true);
        _repeats = AddField("Repeats", Chips(BillSchedule.All, _target.Repeats));
        var categories = Categories.Spending.ToList();
        if (!categories.Contains(_target.Category, StringComparer.OrdinalIgnoreCase)) categories.Add(_target.Category);
        _category = AddField("Category", CategoryChips(categories, _target.Category));
        _account = AddField("Paid from", Chips(DataStore.AccountChoices(_target.Account), _target.Account));
        _autoPay = AddField("Auto-pay", Check("This bill pays itself automatically", _target.AutoPay));

        // Pays down a debt (student loan, car loan, card…) or not (insurance, subscriptions, utilities)
        var debts = DataStore.Debts.Items.Where(d => !d.IsPaidOff || d.Id == _target.DebtId).OrderBy(d => d.Name).ToList();
        if (debts.Count > 0)
        {
            var choices = new List<(string, string)> { ("", "No, a regular bill") };
            choices.AddRange(debts.Select(d => (d.Id, d.Name)));
            _debt = new WrapPanel();
            SetChips(_debt, choices, _target.DebtId);
            AddField("Pays down a debt", _debt,
                hint: "For a loan or card payment. Mark paid lowers that debt's balance, and the bill ends when it's paid off. Insurance, subscriptions and utilities aren't debts.");
            foreach (var chip in _debt.Children.OfType<RadioButton>())
                chip.Checked += (_, _) =>
                {
                    if (DataStore.Debts.Find(Selected(_debt) ?? "") is not { } d) return;
                    // Fill in what's still empty from the debt
                    if (_name.Text.Trim().Length == 0) _name.Text = d.Name;
                    if (_amount.Text.Trim().Length == 0 && d.MinimumPayment > 0) _amount.Text = d.MinimumPayment.ToString("0.00", Money.Us);
                    SetCategoryChips(_category, categories.Contains(Categories.DebtPayments) ? categories : categories.Append(Categories.DebtPayments), Categories.DebtPayments);
                    if (Selected(_repeats) == BillSchedule.Once) SetChips(_repeats, BillSchedule.All, BillSchedule.Monthly);
                };
        }
        _note = AddField("Note", new TextBox { Text = _target.Note });
        Loaded += (_, _) => _name.Focus();
    }

    protected override string? TrySave()
    {
        string name = _name.Text.Trim();
        if (name.Length == 0) return "Enter a name for the bill.";
        if (!ReadAmount(_amount, allowZero: false, out var amount)) return "Enter the amount, like 120.00.";
        if (_due.SelectedDate is not DateTime due) return "Pick when it's due.";

        _target.Name = name;
        _target.Amount = amount;
        _target.NextDue = due.Date;
        _target.DueDay = due.Day;
        string repeats = Selected(_repeats) ?? BillSchedule.Monthly;
        // A paid one-time bill becomes due again if it's switched to repeating or given a later due date.
        if (_target.IsDone && (repeats != BillSchedule.Once || due.Date > _target.LastPaid!.Value.Date)) _target.LastPaid = null;
        _target.Repeats = repeats;
        _target.Category = Selected(_category) ?? "Other";
        _target.Account = Selected(_account) ?? DataStore.AccountChoices()[0];
        _target.AutoPay = _autoPay.IsChecked == true;
        _target.Note = _note.Text.Trim();
        string debtId = _debt == null ? _target.DebtId : Selected(_debt) ?? "";
        if (debtId.Length > 0 && DataStore.Bills.Items.FirstOrDefault(b => b != _target && b.DebtId == debtId && !b.IsDone) is { } other)
            return $"{DataStore.Debts.Find(debtId)?.Name ?? "That debt"} is already paid by the bill \"{other.Name}\". Edit that one instead.";
        _target.DebtId = debtId;
        return null;
    }

    public static bool Edit(Bill? existing, Debt? forDebt = null)
    {
        var dialog = new BillDialog(existing, forDebt);
        if (dialog.ShowDialog() != true) return false;
        if (existing == null)
        {
            DataStore.Bills.Add(dialog.Result);
            Toaster.Show($"Added {dialog.Result.Name}, due {dialog.Result.NextDue:MMM d}.");
        }
        else if (dialog.DeleteRequested)
        {
            DataStore.Bills.Delete(existing);
            Toaster.Show($"Deleted {existing.Name}.");
        }
        else
        {
            DataStore.Bills.Update(existing);
            Toaster.Show("Bill saved.");
        }
        return true;
    }
}

/// <summary>Add or edit a savings goal.</summary>
public sealed class GoalDialog : DialogShell
{
    private readonly Goal _target;
    private readonly TextBox _name;
    private readonly TextBox _amount;
    private readonly TextBox _saved;
    private readonly DatePicker _date;
    private readonly TextBox _note;
    private readonly WrapPanel _link;
    private readonly WrapPanel? _repeats;
    private const string NotLinked = "Not linked";

    public Goal Result => _target;

    public GoalDialog(Goal? existing, bool fund = false)
        : base((existing?.IsFund ?? fund) ? (existing == null ? "New sinking fund" : "Edit sinking fund") : existing == null ? "New goal" : "Edit goal",
               (existing?.IsFund ?? fund)
                   ? "For a cost that comes around again, like car registration or holidays. WillowWallet works out how much to set aside each month."
                   : existing == null ? "Something to save toward, like an emergency fund, a trip or a new car." : "Change the goal, or delete it.",
               allowDelete: existing != null, saveText: existing != null ? "Save" : fund ? "Create sinking fund" : "Create goal")
    {
        _target = existing ?? new Goal { IsFund = fund };
        bool isFund = _target.IsFund;
        _name = AddField("Name", new TextBox { Text = _target.Name }, required: true);
        _amount = AddField(isFund ? "What it costs" : "Target amount", AmountBox(existing?.Target), required: true);
        _saved = AddField(isFund ? "Set aside so far" : "Saved so far", AmountBox(existing?.Saved), hint: "Leave empty if you're starting from zero.");
        _date = AddField(isFund ? "Next due" : "Target date", DateBox(_target.TargetDate), required: isFund,
            hint: isFund ? null : "Optional. With a date, WillowWallet works out how much to save each month.");
        if (isFund) _repeats = AddField("Comes around", Chips(FundSchedule.All, _target.Repeats));
        var accounts = DataStore.Accounts.Items.Where(a => !a.IsClosed && !a.IsLiability)
            .OrderBy(a => a.Type == AccountTypes.Savings ? 0 : 1).Select(a => a.Name).ToList();
        _link = AddField("Linked account", Chips(new[] { NotLinked }.Concat(accounts), _target.IsLinked ? _target.LinkedAccount : NotLinked),
            hint: accounts.Count == 0
                ? "Add a savings account on the Accounts page to link it here."
                : "Linked: the goal's saved amount is that account's balance, so it updates by itself.");
        foreach (var chip in _link.Children.OfType<RadioButton>())
            chip.Checked += (_, _) => ShowField(_saved, Selected(_link) == NotLinked);
        ShowField(_saved, !_target.IsLinked);
        _note = AddField("Note", new TextBox { Text = _target.Note });
        Loaded += (_, _) => _name.Focus();
    }

    protected override string? TrySave()
    {
        string name = _name.Text.Trim();
        if (name.Length == 0) return "Enter a name for the goal.";
        if (!ReadAmount(_amount, allowZero: false, out var target)) return _target.IsFund ? "Enter what it costs, like 360." : "Enter the target amount, like 5000.";
        if (_target.IsFund && _date.SelectedDate == null) return "Pick when it's next due.";
        if (_repeats != null) _target.Repeats = Selected(_repeats) ?? FundSchedule.Yearly;
        string link = Selected(_link) ?? NotLinked;
        bool linked = link != NotLinked;
        decimal saved = _target.Saved;
        if (!linked && !ReadAmount(_saved, allowZero: true, out saved)) return "Enter what you've saved so far as a number (or leave it empty).";

        if (!linked && saved != _target.Saved && _target.Id.Length > 0)
            _target.History.Add(new MoneyEntry { Date = DateTime.Today, Amount = saved - _target.Saved });
        _target.LinkedAccount = linked ? link : "";
        _target.Name = name;
        _target.Target = target;
        _target.Saved = saved;
        _target.TargetDate = _date.SelectedDate?.Date;
        _target.Note = _note.Text.Trim();
        return null;
    }

    public static bool Edit(Goal? existing, bool fund = false)
    {
        var dialog = new GoalDialog(existing, fund);
        if (dialog.ShowDialog() != true) return false;
        if (existing == null)
        {
            DataStore.Goals.Add(dialog.Result);
            GoalMath.RefreshLinked();
            Toaster.Show(dialog.Result.IsFund ? $"Sinking fund created: {dialog.Result.Name}." : $"Goal created: {dialog.Result.Name}.");
        }
        else if (dialog.DeleteRequested)
        {
            DataStore.Goals.Delete(existing);
            Toaster.Show($"Deleted {existing.Name}.");
        }
        else
        {
            DataStore.Goals.Update(existing);
            Toaster.Show("Goal saved.");
        }
        return true;
    }
}

/// <summary>Add money to a goal, or take some out.</summary>
public sealed class GoalMoneyDialog : DialogShell
{
    private readonly WrapPanel _kind;
    private readonly TextBox _amount;
    private readonly DatePicker _date;

    public decimal Change { get; private set; }
    public DateTime Date { get; private set; }

    public GoalMoneyDialog(Goal goal)
        : base(goal.Name, $"{Money.Format(goal.Saved)} saved of {Money.Format(goal.Target, cents: false)}.", allowDelete: false, saveText: "Save")
    {
        _kind = AddField("Add or take out", Chips(["Add money", "Take money out"], "Add money"));
        _amount = AddField("Amount", AmountBox(null), required: true);
        _date = AddField("Date", DateBox(DateTime.Today));
        Loaded += (_, _) => _amount.Focus();
    }

    protected override string? TrySave()
    {
        if (!ReadAmount(_amount, allowZero: false, out var amount)) return "Enter an amount, like 100.";
        Change = Selected(_kind) == "Take money out" ? -amount : amount;
        Date = _date.SelectedDate?.Date ?? DateTime.Today;
        return null;
    }

    public static bool Open(Goal goal)
    {
        var dialog = new GoalMoneyDialog(goal);
        if (dialog.ShowDialog() != true) return false;
        bool wasReached = goal.IsReached;
        decimal before = goal.Saved;
        GoalMath.AddMoney(goal, dialog.Change, dialog.Date);
        int? milestone = GoalMath.MilestoneCrossed(before, goal.Saved, goal.Target);
        Toaster.Show(!wasReached && goal.IsReached ? $"You reached {goal.Name}. Congratulations!"
            : milestone is int m ? $"Milestone: {goal.Name} is {m}% of the way there."
            : dialog.Change > 0 ? $"Added {Money.Format(dialog.Change)} to {goal.Name}."
            : $"Took {Money.Format(-dialog.Change)} out of {goal.Name}.");
        return true;
    }
}
