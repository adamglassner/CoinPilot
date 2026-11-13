# Changelog

## 1.0.2 — 2026-11-13

**CoinPilot is now WillowWallet.** Same app, same icon, new name.

- The program is now `WillowWallet.exe`, with Start Menu and desktop shortcuts under the new name. Updating from
  CoinPilot (installer or the Bytehawk Studio Launcher) replaces the old program and shortcuts.
- Your data moves from `%AppData%\CoinPilot` to `%AppData%\WillowWallet` the first time WillowWallet opens. Nothing
  else changes: budgets, transactions, receipts, tax documents and your PIN/password all carry over. Older
  "CoinPilot backup" files still restore.
- "Run in the background" (start with Windows) is updated to the new program automatically.

## 1.0.1 — 2026-11-13

A more modern look.

- **New dialogs:** every add/edit window is now a floating card with rounded corners, a soft shadow, an icon, its own
  close button and a quick fade-in, and CoinPilot dims behind it. Mistakes show as a clear banner, Delete has an icon,
  and warnings (like restoring a backup) get a red badge. Tall dialogs fit small laptop screens.
- **Big numbers fit:** amounts on the summary tiles shrink to fit instead of being cut off with "…".
- **Goals:** names get their own line, and the milestone chips wrap instead of running off the card.
- **Transactions:** the search box no longer runs under the Filters button, and dates show in full.
- Screenshots of every page and dialog are published to the `screenshots` branch with each build.

## 1.0.0 — 2026-11-13

CoinPilot 1.0: everything is finished except syncing with the mobile app (and the sign-in that goes with it).

**New**
- **Taxes:** a safe place for tax documents (W-2s, 1099s, your filed return, receipts), organized by tax year, with a
  checklist of the common forms. Add files with a button or by dragging them onto the page; open or save a copy any time.
  CoinPilot keeps its own copy, so the documents are safe even if you delete the originals.
- **PIN or password protection:** encrypts everything CoinPilot saves (transactions, accounts, receipts and tax documents)
  with AES-256. You unlock CoinPilot when you open it, it can lock itself after a few minutes away, and a one-time
  **recovery key** gets you in if you forget the PIN/password. Change or turn it off in Settings → Security.
- **Color themes:** Pine (the original green), Ocean, Lagoon, Indigo, Plum, Rose, Ember and Graphite, each in light and dark.

**Transactions**
- **Import bank and card statements** (CSV from any bank, OFX and QFX). CoinPilot guesses the columns, previews the
  transactions, skips ones you already have, categorizes them, and can update the account's balance from the statement.
- **Automatic categories:** CoinPilot remembers the category you use for each payee, and you can add rules
  ("payee contains STARBUCKS → Coffee"). Tick "Always use this category" when adding a transaction to make a rule.
- **Split** a transaction across several categories.
- **Transfers** between accounts (like paying off a credit card) that don't count as spending or income.
- **Tags**, **filters** (type, category, account, tag) and **receipts** (attach a photo or PDF).
- Ctrl+N adds a transaction from anywhere; Ctrl+F searches.

**Budgets**
- **Rollover:** leftover money (or overspending) carries into next month.
- **Templates:** 50/30/20, zero-based (from what you've spent) or starter amounts.
- **One-month amounts** and **Copy last month**, plus "left to assign" (income minus budgeted).

**Accounts, bills, goals, reports**
- Accounts: **net worth over time** chart and **Transfer money**.
- Bills: **reminders** (Windows notifications; CoinPilot can run in the background and start with Windows),
  a **calendar** view, and **possible subscriptions** found in your transactions that aren't on your bills list.
- Goals: **progress rings**, **milestones** (25/50/75/100%) and **linking a goal to a savings account**.
- Reports: **net worth**, a **Year in review**, and **Print / PDF**.

**Settings and polish**
- **Currency** (dollar, pound, euro, rupee, yen and more) and **budget month start day** (budget from payday to payday).
- **Your own categories and subcategories** (rename, hide, add) and rules, in Settings → Categories.
- **Back up and restore** everything (including receipts and tax documents) as one .zip, and export all transactions.
- A welcome checklist for new users, redesigned controls (inputs, dropdowns, check boxes, scrollbars, menus), smooth page
  transitions, hover effects, keyboard shortcuts, CoinPilot remembers its window size and position, and opening it
  again brings the running window to the front.

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
