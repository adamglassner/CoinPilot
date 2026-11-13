using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>The Accounts page: every account's balance (from its starting balance plus transactions) and net worth.</summary>
public sealed class AccountsView : DataPage
{
    private static readonly double[] Cols = [2.6, 1.2, 1.3, 1.3];

    protected override void Build()
    {
        var accounts = DataStore.Accounts.Items;
        var open = accounts.Where(a => !a.IsClosed).ToList();
        var balances = accounts.ToDictionary(a => a, AccountMath.Balance);

        Add(PageKit.Header("", "Accounts", "Checking, savings, credit cards and cash, together."));
        Add(PageKit.ActionBar("Balances update automatically from your transactions and transfers.",
            PageKit.Ghost("\uE8AB", "Transfer money", (_, _) => TransferDialogOpen()),
            PageKit.Primary("\uE710", "Add account", (_, _) => AccountDialog.Edit(null))));

        decimal assets = open.Where(a => !a.IsLiability).Sum(a => balances[a]);
        decimal cards = open.Where(a => a.IsLiability).Sum(a => -balances[a]);
        decimal debts = DataStore.Debts.Items.Sum(d => Math.Max(0, d.Balance));
        Add(PageKit.KpiRow(
            PageKit.Kpi("Net worth", Money.Format(AccountMath.NetWorth()), "Accounts minus card balances and debts", hero: true),
            PageKit.Kpi("Cash & savings", Money.Format(assets), $"{open.Count(a => !a.IsLiability)} accounts"),
            PageKit.Kpi("Credit cards owed", Money.Format(cards), $"{open.Count(a => a.IsLiability)} cards"),
            PageKit.Kpi("Debts", Money.Format(debts), "From the Debts page")));

        // ----- Net worth over time -----
        if (accounts.Count > 0)
        {
            var history = AccountMath.NetWorthHistory(12);
            var change = history[^1].Value - history[0].Value;
            var changeText = Ui.Text((change >= 0 ? "Up " : "Down ") + Money.Format(Math.Abs(change), cents: false) + " in 12 months",
                weight: FontWeights.SemiBold, brushKey: change >= 0 ? "Brush.Positive" : "Brush.Danger");
            Add(PageKit.Card(Charts.LineChart(history.Select(p => (p.Date.ToString("MMM", Money.Us), p.Value)).ToList(), 190),
                "Net worth over time", changeText));
        }

        // ----- Open accounts, grouped by type -----
        var list = new StackPanel();
        if (open.Count == 0)
        {
            list.Children.Add(Ui.EmptyState("", "No accounts yet",
                "Add your checking, savings, credit cards and cash. Enter today's balance and CoinPilot keeps it up to date from your transactions.",
                "Add account", (_, _) => AccountDialog.Edit(null)));
        }
        else
        {
            list.Children.Add(HeaderRow());
            foreach (var type in AccountTypes.All)
            {
                foreach (var a in open.Where(a => a.Type == type).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
                    list.Children.Add(BuildRow(a, balances[a]));
            }
        }
        Add(PageKit.Card(list, padding: new Thickness(14, 8, 14, 8)));

        // ----- Account names used in transactions that aren't set up -----
        var known = accounts.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = DataStore.Transactions.Items
            .Where(t => t.Account.Length > 0 && !known.Contains(t.Account))
            .GroupBy(t => t.Account, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.First().Account, Count: g.Count()))
            .OrderBy(x => x.Name).ToList();
        if (unknown.Count > 0)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "These names are used in your transactions but aren't set up as accounts yet. Set them up to track their balances.",
                Style = Res("Muted"), Margin = new Thickness(0, 0, 0, 8),
            });
            foreach (var (name, count) in unknown)
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var link = PageKit.Link("Set up", () => AccountDialog.Edit(null, name));
                DockPanel.SetDock(link, Dock.Right);
                row.Children.Add(link);
                var c = Ui.Text(count == 1 ? "1 transaction" : $"{count} transactions", muted: true);
                c.Margin = new Thickness(0, 0, 12, 0);
                DockPanel.SetDock(c, Dock.Right);
                row.Children.Add(c);
                row.Children.Add(Ui.Text(name, weight: FontWeights.SemiBold));
                panel.Children.Add(row);
            }
            Add(PageKit.Card(panel, "Not set up yet"));
        }

        // ----- Closed accounts -----
        var closed = accounts.Where(a => a.IsClosed).OrderBy(a => a.Name).ToList();
        if (closed.Count > 0)
        {
            var panel = new StackPanel();
            bool first = true;
            foreach (var a in closed)
            {
                panel.Children.Add(BuildRow(a, balances[a], first));
                first = false;
            }
            Add(PageKit.Card(panel, "Closed accounts", padding: new Thickness(14, 14, 14, 8)));
        }
    }

    private static void TransferDialogOpen()
    {
        var dialog = new TransactionDialog(null, DateTime.Today);
        dialog.PresetTransfer();
        if (dialog.ShowDialog() == true)
        {
            DataStore.Transactions.Add(dialog.Result);
            Toaster.Show($"Moved {Money.Format(dialog.Result.Size)} from {dialog.Result.Account} to {dialog.Result.TransferAccount}.");
        }
    }

    private static Grid HeaderRow()
    {
        var g = Ui.Columns(Cols);
        g.Margin = new Thickness(0, 6, 0, 8);
        string[] names = ["ACCOUNT", "TYPE", "LAST ACTIVITY", "BALANCE"];
        for (int i = 0; i < names.Length; i++)
        {
            var t = Ui.Text(names[i], muted: true, size: 11, weight: FontWeights.SemiBold).At(i, new Thickness(10, 0, 10, 0));
            if (i == names.Length - 1) t.HorizontalAlignment = HorizontalAlignment.Right;
            g.Children.Add(t);
        }
        return g;
    }

    private static Button BuildRow(Account a, decimal balance, bool firstRow = false)
    {
        var g = Ui.Columns(Cols);
        var cell = new Thickness(10, 12, 10, 12);

        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center }.At(0, cell);
        name.Children.Add(Ui.Text(a.Name, weight: FontWeights.SemiBold));
        if (a.Institution.Length > 0) name.Children.Add(Ui.Text(a.Institution, muted: true, size: 12));
        g.Children.Add(name);

        g.Children.Add(Ui.Chip(a.Type).At(1, cell));
        var last = AccountMath.LastActivity(a);
        g.Children.Add(Ui.Text(last is DateTime d ? d.ToString("MMM d, yyyy", Money.Us) : "No transactions", muted: true).At(2, cell));

        var bal = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }.At(3, cell);
        if (a.IsLiability)
        {
            var owed = Ui.Text(Money.Format(Math.Max(0, -balance)), weight: FontWeights.SemiBold, brushKey: -balance > 0 ? "Brush.Danger" : "Brush.Text");
            owed.HorizontalAlignment = HorizontalAlignment.Right;
            bal.Children.Add(owed);
            var label = Ui.Text(balance > 0 ? $"{Money.Format(balance)} credit" : "owed", muted: true, size: 12);
            label.HorizontalAlignment = HorizontalAlignment.Right;
            bal.Children.Add(label);
        }
        else
        {
            var b = Ui.Text(Money.Format(balance), weight: FontWeights.SemiBold, brushKey: balance < 0 ? "Brush.Danger" : "Brush.Text");
            b.HorizontalAlignment = HorizontalAlignment.Right;
            bal.Children.Add(b);
        }
        g.Children.Add(bal);

        return PageKit.Row(g, $"{a.Name}, {a.Type}, balance {Money.Format(balance)}", () => AccountDialog.Edit(a), firstRow);
    }
}
