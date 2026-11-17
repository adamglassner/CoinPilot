using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>Shared household: who paid what this month, each person's share, and who owes whom.</summary>
public sealed class HouseholdView : DataPage
{
    public static string Glyph => "";

    protected override void Build()
    {
        var prefs = DataStore.Prefs;
        if (!prefs.Household)
        {
            Add(PageKit.Header(Glyph, "Household", "Share spending with a partner or roommate."));
            Add(PageKit.Card(Ui.EmptyState(Glyph, "Set up your household",
                "Add the people who share expenses. Each transaction then says who paid, and WillowWallet works out who owes whom. You both sign in to the same WillowWallet account on your own devices, so everything stays in sync.",
                "Set up", (_, _) => { if (HouseholdDialog.Edit()) Rebuild(); })));
            return;
        }
        var month = MonthSwitcher.Current;
        var h = Insights.Household(month);
        var switcher = new MonthSwitcher();
        switcher.MonthChanged += (_, _) => Rebuild();
        Add(PageKit.Header(Glyph, "Household",
            $"{string.Join(" and ", prefs.HouseholdPeople)} · {(prefs.HouseholdPeople.Count == 2 ? $"{prefs.HouseholdShare}/{100 - prefs.HouseholdShare} split" : "even split")}", switcher));
        Add(PageKit.ActionBar("Pick who paid when you add a transaction. \"Not shared\" keeps it out of this.",
            PageKit.Ghost("", "Edit household", (_, _) => { if (HouseholdDialog.Edit()) Rebuild(); })));

        var tiles = new List<Border>
        {
            PageKit.Kpi("Who owes whom", h.Settle.Count == 0 ? (h.Total == 0 ? "Nothing yet" : "All even") : string.Join("\n", h.Settle.Select(s => $"{s.From} → {s.To} {Money.Format(s.Amount)}")),
                $"Shared spending {Money.Format(h.Total, cents: false)}", hero: true),
        };
        foreach (var person in h.People)
            tiles.Add(PageKit.Kpi($"{person} paid", Money.Format(h.Paid[person], cents: false), $"Share {Money.Format(h.Fair[person], cents: false)}"));
        Add(PageKit.KpiRow(tiles.Take(4).ToArray()));

        var list = new StackPanel();
        bool first = true;
        foreach (var t in h.Transactions.Take(60))
        {
            var row = Ui.Columns(3, 2, 1);
            row.Children.Add(Ui.Text(t.Payee, weight: FontWeights.SemiBold).At(0));
            row.Children.Add(Ui.Text($"{t.Date:MMM d} · paid by {t.PaidBy}", muted: true).At(1));
            row.Children.Add(Ui.Text(Money.Format(-t.Amount)).Also(x => x.HorizontalAlignment = HorizontalAlignment.Right).At(2));
            var tt = t;
            list.Children.Add(PageKit.Row(row, $"{t.Payee} paid by {t.PaidBy}", () => TransactionDialog.Edit(tt), first));
            first = false;
        }
        if (h.Transactions.Count == 0) list.Children.Add(Ui.Text("Nothing shared this month yet.", muted: true));
        Add(PageKit.Card(list, "Shared this month", padding: new Thickness(14, 14, 14, 8)));
    }
}

/// <summary>The people who share expenses, and how shared spending is split.</summary>
public sealed class HouseholdDialog : DialogShell
{
    private readonly TextBox _names;
    private readonly WrapPanel _split;

    public HouseholdDialog()
        : base("Household", "The people who share expenses, and how shared spending is split.", allowDelete: false, saveText: "Save", width: 480)
    {
        var prefs = DataStore.Prefs;
        _names = AddField("People", new TextBox { Text = string.Join(", ", prefs.HouseholdPeople) }, hint: "Names separated by commas, like \"Alex, Sam\". Clear it to turn the household off.");
        _split = AddField("Split (when there are two)", Chips(["50/50", "60/40", "70/30", "40/60", "30/70"], $"{prefs.HouseholdShare}/{100 - prefs.HouseholdShare}"),
            hint: "The first person's share, then the second's. With three or more people it's split evenly.");
        Loaded += (_, _) => _names.Focus();
    }

    protected override string? TrySave()
    {
        var people = _names.Text.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
        if (people.Count == 1) return "Add at least two people, or clear the names to turn it off.";
        if (people.Distinct(StringComparer.OrdinalIgnoreCase).Count() != people.Count) return "Use a different name for each person.";
        DataStore.Prefs.HouseholdPeople = people;
        DataStore.Prefs.HouseholdShare = int.Parse((Selected(_split) ?? "50/50").Split('/')[0]);
        DataStore.Prefs.Save();
        return null;
    }

    public static bool Edit() => new HouseholdDialog { Owner = MainWindow.Instance }.ShowDialog() == true;
}
