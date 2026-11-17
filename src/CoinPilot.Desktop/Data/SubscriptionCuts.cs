using System.Globalization;
using CoinPilot.Services;

namespace CoinPilot.Data;

/// <summary>A subscription you pay for: one on the Bills page, or a repeating charge found in your transactions.</summary>
public sealed record Subscription(string Key, string Name, decimal Amount, string Repeats, decimal Monthly, DateTime NextCharge, Bill? Bill, SubscriptionGuess? Guess);

/// <summary>A subscription worth cutting, and why.</summary>
public sealed record CutSuggestion(Subscription Sub, string Reason, int Priority)
{
    public string Key => Sub.Key;
    public decimal MonthlySaving => Math.Round(Sub.Monthly, 2);
    public decimal YearlySaving => Math.Round(Sub.Monthly * 12, 2);
}

/// <summary>
/// "Subscriptions you could cut" on the Budgets page. Looks at your subscriptions (Bills in Subscriptions, and repeating
/// charges from known services) and suggests ones to cut: services that overlap (three streaming services, two music
/// apps), prices that went up, a yearly renewal coming soon, and the priciest ones when the Subscriptions budget is
/// over. Suggestions can be skipped for a month or for good (kept in Preferences.SkippedCuts).
/// </summary>
public static class SubscriptionCuts
{
    /// <summary>Kinds of services people often pay for twice.</summary>
    public static readonly (string Label, string Plural, string[] Names)[] Groups =
    [
        ("streaming service", "streaming services", ["netflix", "hulu", "disney", "max", "hbo", "peacock", "paramount", "apple tv", "prime video", "youtube premium", "youtube tv", "sling", "fubo", "espn", "crunchyroll", "starz", "showtime", "discovery", "britbox", "philo", "mubi"]),
        ("music app", "music apps", ["spotify", "apple music", "youtube music", "amazon music", "pandora", "tidal", "siriusxm", "sirius xm", "deezer"]),
        ("cloud storage plan", "cloud storage plans", ["icloud", "google one", "google storage", "dropbox", "onedrive", "microsoft 365", "box.com", "pcloud"]),
        ("news subscription", "news subscriptions", ["new york times", "nytimes", "wsj", "wall street journal", "washington post", "the athletic", "medium", "substack", "apple news", "economist"]),
        ("meal kit", "meal kits", ["hellofresh", "home chef", "blue apron", "factor", "everyplate", "green chef", "sunbasket"]),
        ("game subscription", "game subscriptions", ["xbox", "game pass", "playstation plus", "ps plus", "nintendo", "ea play", "ubisoft"]),
        ("delivery membership", "delivery memberships", ["dashpass", "doordash", "uber one", "instacart+", "instacart plus", "grubhub+", "walmart+", "walmart plus"]),
        ("fitness app", "fitness apps", ["peloton", "apple fitness", "strava", "beachbody", "noom", "fitbit premium", "myfitnesspal"]),
    ];

    /// <summary>Names that are subscriptions even when they're filed under another category.</summary>
    private static bool IsKnownService(string name)
    {
        string n = name.ToLowerInvariant();
        return Groups.Any(g => g.Names.Any(s => Matches(n, s)));
    }

    private static bool Matches(string name, string service) =>
        service.Length <= 4
            ? name.Split(' ', '.', '*', '-', '/').Contains(service) // "max", "wsj", "box": whole words only
            : name.Contains(service);

    private static string? GroupOf(string name)
    {
        string n = name.ToLowerInvariant();
        return Groups.FirstOrDefault(g => g.Names.Any(s => Matches(n, s))).Label;
    }

    public static string KeyFor(string name) => SubscriptionFinder.Normalize(name);

    /// <summary>Every subscription: Bills in Subscriptions (or known services), then repeating charges found in Transactions.</summary>
    public static List<Subscription> All(DateTime today)
    {
        var list = new List<Subscription>();
        foreach (var b in DataStore.Bills.Items.Where(b => !b.IsDone && !b.PaysDebt && b.Repeats != BillSchedule.Once
                     && (string.Equals(b.Category, "Subscriptions", StringComparison.OrdinalIgnoreCase) || IsKnownService(b.Name))))
            list.Add(new Subscription(KeyFor(b.Name), b.Name, b.Amount, b.Repeats, BillSchedule.MonthlyCost(b), b.NextDue, b, null));
        foreach (var g in SubscriptionFinder.Find(today).Where(g => string.Equals(g.Category, "Subscriptions", StringComparison.OrdinalIgnoreCase) || IsKnownService(g.Payee)))
        {
            string key = KeyFor(g.Payee);
            if (list.Any(s => s.Key == key)) continue;
            list.Add(new Subscription(key, Pretty(g.Payee), g.Amount, g.Repeats,
                BillSchedule.MonthlyCost(new Bill { Amount = g.Amount, Repeats = g.Repeats }), g.NextDue, null, g));
        }
        return list;
    }

    /// <summary>"NETFLIX.COM" → "Netflix.com" for display.</summary>
    private static string Pretty(string payee)
    {
        string p = payee.Trim();
        return p.Any(char.IsLower) ? p : CultureInfo.GetCultureInfo("en-US").TextInfo.ToTitleCase(p.ToLowerInvariant());
    }

    /// <summary>Skipped for <paramref name="month"/> (budget month) or for good.</summary>
    public static bool IsSkipped(string key, DateTime month)
    {
        var skipped = DataStore.Prefs.SkippedCuts;
        return skipped.Contains($"{key}|forever") || skipped.Contains($"{key}|{month:yyyy-MM}");
    }

    public static void Skip(string key, DateTime? month)
    {
        var prefs = DataStore.Prefs;
        string entry = month is DateTime m ? $"{key}|{m:yyyy-MM}" : $"{key}|forever";
        if (!prefs.SkippedCuts.Contains(entry)) prefs.SkippedCuts.Add(entry);
        // Month skips older than a year are no longer needed
        prefs.SkippedCuts.RemoveAll(e => e.Length > 8 && DateTime.TryParseExact(e[^7..], "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d < DateTime.Today.AddYears(-1));
        prefs.Save();
    }

    /// <summary>Brings back everything that was skipped.</summary>
    public static void ResetSkips()
    {
        DataStore.Prefs.SkippedCuts.Clear();
        DataStore.Prefs.Save();
    }

    /// <summary>"I canceled it": the bill ends, the charge stops being suggested, and it's not suggested again.</summary>
    public static void MarkCanceled(Subscription s)
    {
        if (s.Bill is { } b) DataStore.Bills.Delete(b);
        if (s.Guess is { } g && !DataStore.Prefs.DismissedSubscriptions.Contains(g.Payee, StringComparer.OrdinalIgnoreCase))
            DataStore.Prefs.DismissedSubscriptions.Add(g.Payee);
        Skip(s.Key, null);
    }

    /// <summary>Suggestions for a budget month, best first. Skipped ones are left out.</summary>
    public static List<CutSuggestion> Suggest(DateTime month, DateTime today)
    {
        var subs = All(today);
        var found = new Dictionary<string, CutSuggestion>();
        void Offer(Subscription s, string reason, int priority)
        {
            if (s.Monthly <= 0) return;
            if (!found.TryGetValue(s.Key, out var old) || old.Priority < priority) found[s.Key] = new CutSuggestion(s, reason, priority);
        }

        // 1. Overlapping services: keep the one you pay most for, suggest the others
        foreach (var group in subs.GroupBy(s => GroupOf(s.Name)).Where(g => g.Key != null && g.Count() >= 2))
        {
            var label = Groups.First(x => x.Label == group.Key);
            var ordered = group.OrderByDescending(s => s.Monthly).ToList();
            string names = string.Join(", ", ordered.Select(s => s.Name));
            foreach (var s in ordered.Skip(1))
                Offer(s, $"You pay for {ordered.Count} {label.Plural} ({names}). Keeping one and switching between them when there's something to watch saves money.", 40);
        }

        // 2. The price went up
        foreach (var s in subs)
        {
            var charges = DataStore.Transactions.Items
                .Where(t => t.IsSpending && t.Date >= today.AddMonths(-13) && KeyFor(t.Payee) == s.Key)
                .OrderBy(t => t.Date).Select(t => -t.Amount).ToList();
            if (charges.Count < 2) continue;
            decimal first = charges[0], last = charges[^1];
            if (last - first >= 1m && last >= first * 1.10m)
                Offer(s, $"The price went up from {Money.Format(first)} to {Money.Format(last)}. A good moment to ask whether it's still worth it, or to look for a cheaper plan.", 35);
        }

        // 3. A yearly renewal coming up
        foreach (var s in subs.Where(s => s.Repeats == BillSchedule.Yearly && s.Amount >= 30m))
        {
            int days = (s.NextCharge.Date - today.Date).Days;
            if (days is >= 0 and <= 45)
                Offer(s, $"Renews on {s.NextCharge:MMMM d} for {Money.Format(s.Amount)}. Cancel before then if you don't use it much.", 30);
        }

        // 4. Over the Subscriptions budget: the priciest ones
        var summary = Budgeting.Summarize(month);
        var subBudget = summary.Budgets.FirstOrDefault(b => string.Equals(b.Budget.Category, "Subscriptions", StringComparison.OrdinalIgnoreCase));
        if (subBudget != null && subBudget.Spent > subBudget.Available)
            foreach (var s in subs.OrderByDescending(s => s.Monthly).Take(2))
                Offer(s, $"Your Subscriptions budget is over by {Money.Format(subBudget.Spent - subBudget.Available)} this month. This is one of the biggest.", 25);

        // 5. The pricey ones: what they cost in a year
        foreach (var s in subs.Where(s => s.Monthly >= 15m))
            Offer(s, $"Costs about {Money.Format(Math.Round(s.Monthly * 12), cents: false)} a year. If you don't use it every week, it may not be worth it.", 10);

        return found.Values
            .Where(c => !IsSkipped(c.Key, month))
            .OrderByDescending(c => c.Priority).ThenByDescending(c => c.MonthlySaving)
            .ToList();
    }
}
