# Changelog

## 0.3.0 — 2026-11-13

Every section now works except sync with the mobile app.

- **Accounts:** add checking, savings, credit cards, cash and investment accounts with a balance as of a date.
  Balances then update from your transactions. Shows net worth (accounts minus card balances and debts), lists
  account names used in transactions that aren't set up yet ("Set up"), and keeps closed accounts out of the way.
  Renaming an account renames it in its transactions and bills. New transactions pick from your accounts.
- **Debts (new):** track credit cards and loans with the amount owed, interest rate (APR) and minimum payment.
  Record payments (optionally added to Transactions under the new **Debt payments** category). A payoff plan
  shows your debt-free date and the interest you'll pay, using **avalanche** (highest rate first) or **snowball**
  (smallest balance first) with an optional extra monthly amount, and compares the two.
- **Bills:** add bills and subscriptions with an amount, due date and schedule (weekly, every 2 weeks, monthly,
  every 3 months, yearly or one time). See what's overdue, due soon and still to pay this month, plus your monthly
  bill and subscription totals. **Mark paid** adds the payment to Transactions and moves the bill to its next due date.
- **Goals:** savings goals with a target amount and optional date, progress bars, add or take out money, and the
  monthly amount needed to hit the date.
- **Reports:** income vs. spending by month, spending by category with percentages, a month-by-month table with
  savings rate, and averages for the last 3, 6 or 12 months or this year. **Export** saves the table as CSV.
- **Dashboard:** new "Coming up" (bills due in the next two weeks) and "Goals" cards.
- New spending categories: **Debt payments** and **Savings**.
- The sidebar scrolls on short windows.

## 0.2.0 — 2026-11-13

The first two features are here: **Transactions** and **Budgets**. Everything is saved on this PC.

- **Transactions:** add spending and income with amount, payee, category, account, date and a note. Click any
  transaction to edit or delete it. Browse month by month, search by payee, category, account, note or amount,
  and see the month's spent, income and net totals. **Export** saves the month as a CSV file that opens in Excel.
- **Budgets:** set a monthly amount for any spending category (a suggested amount fills in to start). Each budget
  shows spent, left and a progress bar that turns amber at 85% and red when you go over. The page totals what's left
  to spend and lists spending that isn't covered by a budget, with a one-click "Budget it".
- **Dashboard** now shows your real numbers for this month instead of sample data: left to spend, spent, income,
  net, your budgets (closest to their limit first), spending by week and your latest transactions.
- **Settings → Your data** shows where your data is saved, with an Open data folder button.
- Data lives in `%AppData%\CoinPilot\data\`. Uninstalling keeps it.

## 0.1.1 — 2026-11-11

- New app icon: a gold coin stamped with the CoinPilot heading arrow.

## 0.1.0 — 2026-11-11

First preview of CoinPilot for Windows: the app's shell.

- Sidebar with Dashboard, Transactions, Budgets, Goals, Accounts, Bills, Reports and Sync & Devices.
- Dashboard preview built from the CoinPilot mockup, with sample data: left to spend, spent, income, savings
  toward goals, budget progress bars, spending by week and recent transactions.
- Every other section is a "coming soon" page that describes what it will do and lists its planned features.
- Light and dark mode (sidebar toggle, or Settings → Appearance, including "Match Windows").
- Installer and portable zip, installable and updatable from the Bytehawk Studio Launcher.
