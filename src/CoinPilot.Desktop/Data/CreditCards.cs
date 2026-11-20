namespace CoinPilot.Data;

// Bills charged to a credit card: they add to what you owe on the card, and leave your cash when the card is paid.
// WillowWallet for Android, Mac and Linux does the same (mobile/lib/forecast.dart cardPaymentDate, extras.dart).

public static class CreditCards
{
    /// <summary>The open credit card account with this name, if any.</summary>
    public static Account? Find(string name) =>
        DataStore.Accounts.Items.FirstOrDefault(a => a.IsLiability && !string.IsNullOrEmpty(name) && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The card a bill is charged to, when that card has a payment due day (so the forecast knows when it's paid).</summary>
    public static Account? For(Bill b) => Find(b.Account) is { PaymentDueDay: > 0 } a ? a : null;

    /// <summary>Open bills charged to a card.</summary>
    public static List<Bill> BillsOn(Account card) =>
        DataStore.Bills.Items.Where(b => !b.IsDone && string.Equals(b.Account, card.Name, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// When a charge made on <paramref name="charge"/> gets paid off: the card's first payment due date whose statement
    /// includes it. The statement closes on <paramref name="statementDay"/> (or, without one, 21 days before the due date).
    /// </summary>
    public static DateTime PaymentDate(DateTime charge, int dueDay, int statementDay = 0)
    {
        var c = charge.Date;
        static DateTime On(int y, int m, int d)
        {
            var first = new DateTime(y, 1, 1).AddMonths(m - 1);
            return new DateTime(first.Year, first.Month, Math.Min(d, DateTime.DaysInMonth(first.Year, first.Month)));
        }
        for (int i = 0; i < 4; i++)
        {
            var due = On(c.Year, c.Month + i, dueDay);
            DateTime close;
            if (statementDay > 0)
            {
                close = On(due.Year, due.Month, statementDay);
                if (close >= due) close = On(due.Year, due.Month - 1, statementDay);
            }
            else close = due.AddDays(-21);
            if (close >= c) return due;
        }
        return On(c.Year, c.Month + 4, dueDay);
    }

    /// <summary>What charging a bill to this account means (null when it isn't a credit card).</summary>
    public static string? BillHint(string account)
    {
        if (Find(account) is not { } a) return null;
        return a.PaymentDueDay > 0
            ? $"Charged to {a.Name}: marking it paid adds it to what you owe on the card. The forecast takes it out of your cash when you pay the card (due on the {Paydays.Ordinal(a.PaymentDueDay)})."
            : $"Charged to {a.Name}: marking it paid adds it to what you owe on the card. Add the card's payment due day (edit it under Accounts) so the forecast knows when the money leaves your bank.";
    }
}
