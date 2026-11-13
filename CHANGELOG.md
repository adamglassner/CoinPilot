# Changelog

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
