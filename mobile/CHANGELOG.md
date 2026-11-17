# WillowWallet for Android — changelog

## 1.7.0 — 2026-11-17

- **Forecast.** Where your money is headed: your expected balance every day to the end of the month (or 30, 60 or 90
  days), from your paychecks, bills, budgets, debts and savings. It says whether you'll end over, short or right on
  target, shows the likely range and the lowest point, and what moves the number most. Try **what-ifs** (a one-time
  expense, extra money, skipping a bill, a different budget) without saving anything. Budgets you usually go over are
  forecast at what you usually spend.
- **Safe to spend**: how much you can spend beyond your budgets until the next payday, on the dashboard and Forecast.
- **Bill calendar balances**: each day shows your expected balance, and paydays show up too.
- **Sinking funds** (Goals): set aside a little each month for costs that come around every year, like car
  registration or holidays. "I paid it" starts saving for next time.
- **Spending alerts**, on the dashboard ("Heads up") and as notifications: a budget at 80% (or 70/90%), going over,
  a bill whose price changed (one tap updates the bill), unusually big charges, and a forecast dip. Settings →
  Reminders.
- **Paycheck planner** (Income): what each paycheck needs to cover until the next one, and what's left over.
- **This year vs last** and a **Monthly report** (your month on one page) on Reports.
- **Receipts**: scan a receipt and WillowWallet reads the store, total and date on your device. Every receipt is kept
  on the new Receipts page by year, and can be copied to Taxes.
- **Household**: name who shares expenses and how they're split, pick who paid each transaction, and see who owes whom.
- **Home screen widget**: safe to spend and what's left this month, with a + to add a transaction. Hidden behind
  your PIN when you've set one.
- **Daily check-in** (optional, Settings → Reminders): each evening, "Spent anything today?" Type "12.50 coffee" right
  in the notification.

## 1.6.1 — 2026-11-16

- **Choose your bottom bar.** Pick the three pages beside the + button (Home, Transactions, Budgets, Accounts, Bills,
  Income, Debts, Goals, Reports or Taxes) and their order: More → **Bottom bar**, Settings → Appearance, or
  long-press any button in the bar. Pages that aren't in the bar are under More.
- **Fixed:** with a large font size on your phone, bottom bar labels like "Transactions" wrapped onto two lines and
  were cut off, and so was "WillowWallet" at the top of Home. Labels now stay on one line, the name always fits (the
  sync status shows as just its icon when text is large), page titles shrink to fit, and the tiles on More grow so
  their text fits.

## 1.6.0 — 2026-11-16

- **Unlock with your fingerprint or face.** With a PIN or password on, Settings → Privacy → **Unlock with
  fingerprint** (or face unlock). WillowWallet asks as soon as it opens; your PIN or password still works.
- **Bill reminders as notifications.** A notification on the reminder day (Settings → Reminders: on the day, or 1, 3
  or 7 days before) and on the due day, even when WillowWallet is closed, plus paydays that need an amount. Autopay
  bills aren't reminded. The settings are shared with your PC.
- **What's new** in the app (Settings → Updates → What's new), and once after each update.
- **Same version as the Windows app.** WillowWallet for Android, Windows, Mac and Linux are all 1.6.0 now (this was
  0.3.0) and stay in step.
- Signed with WillowWallet's permanent key, so updates install over each other from Settings → Updates. Coming from
  0.3.0 or earlier: back up (or sync) first, uninstall the old app once, then install this one and restore.
- Needs Android 7.0 or later.

## 0.3.0 — 2026-11-16

**Everything WillowWallet for Windows does, on the phone**, and it all syncs.

- **More tab** with Accounts, Bills, Income, Debts, Goals, Reports, Taxes and Import, plus Sync and Settings.
- **Accounts:** checking, savings, cards, cash, investments and payment apps (PayPal, Venmo, Cash App, Apple Cash),
  balances kept up to date, net worth, transfers between accounts.
- **Bills:** what's overdue and due this week, a calendar, mark paid (adds the payment), skip one, autopay, bills that
  pay down a debt, and repeating charges found in your transactions ("Add as bill" / "Not a bill").
- **Income:** paychecks added on payday when they're the same each time; when they change, WillowWallet asks how much.
- **Debts:** avalanche or snowball payoff plan, extra monthly payment, debt-free date, payments, a monthly bill per debt.
- **Goals:** targets and dates, add or take out money, or link a goal to a savings account.
- **Reports:** income and spending by month, net worth over time, spending by category or payee, and year in review.
- **Taxes:** W-2s, 1099s, receipts and returns by tax year, opened in your PDF or photo viewer.
- **Budgets:** rollover, a different amount for one month, copy last month, templates (50/30/20, zero-based, starter),
  budget months that start on your payday, and **subscriptions you could cut** with Skip ▾ (this month, or for good).
- **Transactions:** transfers, splits across categories, receipt photos (camera or file), tags, and importing bank
  statements (CSV, OFX, QFX; PayPal, Venmo and Cash App downloads recognized; duplicates skipped). Rules pick the
  category from the payee.
- **Settings:** categories and subcategories, rules, budget month, an app lock (PIN or password), export to CSV,
  run Get started again, delete all data.
- **Get started** the first time it opens: accounts, paycheck and budgets, bills, and loans and cards.
- **Backups** in the same .zip as the Windows app: a phone backup restores on the PC and a PC backup restores here
  (protected ones open with the same PIN, password or recovery key).
- **Updates from GitHub:** it checks once a day and in Settings → Updates; "Download and install" hands the new version
  to Android's installer.

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
