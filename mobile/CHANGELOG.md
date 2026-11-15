# WillowWallet for Android — changelog

## 0.2.0 — 2026-11-15

**Sync with WillowWallet for Windows** through your own WillowWallet Server. Optional: without it, everything stays
on the phone as before.

- **Settings → Sync:** enter the server address (it checks the server and says whether you need an invite), then
  sign in or create an account. New accounts get a **recovery key** to save; **Forgot your password?** uses it.
- **Encrypted on the phone** before anything is sent, with the same encryption as the Windows app.
- Transactions and budgets sync both ways; accounts and categories from the PC show up here. The newest edit wins,
  and the phone keeps working offline and catches up later. Syncs a few seconds after each change, every minute,
  and when you come back to the app; pull down on the Sync screen to sync now.
- **Combine or replace** when the phone already has data. See your **devices**, sign one out, change your password,
  or stop syncing (the data stays on the phone).
- The pill at the top of Home shows **Synced**, **Syncing**, **Offline** or **On this phone**; tap it for Sync.
- Transactions from the PC show **transfers** and **splits** properly, and editing one on the phone keeps its splits,
  tags, receipt and other PC-only details. Your own categories and subcategories from the PC are used here.
- Undo after deleting now brings the item back on your other devices too.

## 0.1.1 — 2026-11-13

- **CoinPilot is now WillowWallet.** Same app and icon, new name.

## 0.1.0 — 2026-11-13

First version: the basics, saved on your phone.

- **Home:** left to spend this month, income and net, your budgets, spending by week and recent transactions.
- **Transactions:** add spending or income with the + button (amount, payee, category, account, date, note).
  CoinPilot fills in the category it used last time for the same payee and offers your recent payees.
  Browse by month, search, filter, edit, and swipe to delete with Undo.
- **Budgets:** monthly limits per category with progress bars, totals and not-yet-budgeted spending. New budgets
  suggest an amount from what you've spent lately.
- **Settings:** Auto / Light / Dark, eight color themes (Pine, Ocean, Lagoon, Indigo, Plum, Rose, Ember, Graphite),
  currency, sample data to try it out, and erase everything.
