# CoinPilot

<img src="assets/coinpilot-256.png" width="96" align="right" alt="CoinPilot icon" />

**A budgeting app that keeps your budgets, spending and goals in sync between your PC and your phone.**
Made by Bytehawk Studio.

> **Status: v0.3.0, Windows only.** Every section works and is saved on your PC, except syncing with the CoinPilot
> mobile app, which is coming soon.

## What works

| Section | What it does |
| --- | --- |
| **Dashboard** | This month's left to spend, spent, income and net; budgets; spending by week; bills coming up; goals; recent transactions. |
| **Transactions** | Add, edit and delete spending and income; month by month; search; totals; export to CSV. (Importing statements is coming soon.) |
| **Budgets** | A monthly amount per spending category with progress bars, left to spend, over-budget warnings and "spending without a budget". |
| **Accounts** | Balances for checking, savings, credit cards, cash and investments, kept up to date from transactions; net worth. |
| **Debts** | Credit cards and loans with APR and minimum payments; record payments; avalanche or snowball payoff plan with a debt-free date. |
| **Bills** | Bills and subscriptions with schedules; overdue / due soon; mark paid (adds the transaction and moves to the next due date). |
| **Goals** | Savings goals with progress, add or take out money, and the monthly amount needed to reach the target date. |
| **Reports** | Income vs. spending by month, spending by category, month-by-month table with savings rate; 3/6/12 months or this year; CSV export. |
| Sync & Devices | Coming soon: sync with the CoinPilot mobile app. |
| Appearance | Light / dark mode from the sidebar, or Settings → Appearance → "Match Windows". |

The coming-soon page lists its planned features. They are defined in
[`Models/ModuleCatalog.cs`](src/CoinPilot.Desktop/Models/ModuleCatalog.cs).

## Download

Windows 10/11 (x64). Install it from the **[Bytehawk Studio Launcher](https://github.com/adamglassner/bytehawklauncher)**,
which downloads the newest release, installs it and keeps it up to date. Or get it from the
[latest release](../../releases/latest):

- **`CoinPilot-<version>-Setup.exe`**: installer with Start Menu shortcut, optional desktop shortcut and an
  uninstaller. Installs just for you by default (no administrator prompt).
- **`CoinPilot-<version>-win-x64.zip`**: portable. Unzip anywhere and run `CoinPilot.exe`.

Neither needs .NET installed. Your data (transactions, budgets, accounts, debts, bills and goals) is saved in `%AppData%\CoinPilot\data\`
(Settings → Your data → Open data folder) and settings in `%AppData%\CoinPilot\settings.json`. Uninstalling keeps them.

## Releasing a new version

Change `<Version>` in `src/CoinPilot.Desktop/CoinPilot.Desktop.csproj` (for example to `0.1.1`), add a
[CHANGELOG](CHANGELOG.md) entry and push to `main`. GitHub Actions ([build-release.yml](.github/workflows/build-release.yml))
then, on a Windows machine:

1. builds the single-file exe, the portable zip and the installer,
2. runs the self-test (`CoinPilot.exe --self-test result.txt`) on a throwaway data folder: money parsing, budget math,
   account balances, payoff plans, bill schedules, goals, reports, saving and reloading, every page in light and dark
   (empty and with data), the add/edit dialogs and navigation,
3. silently installs the Setup .exe to a custom folder (the way the launcher does), starts it, and uninstalls it,
4. takes light/dark screenshots (attached to the run as artifacts),
5. publishes GitHub Release `v<version>` with the Setup .exe and the zip.

Installed copies pick up the new version through the Bytehawk Studio Launcher. Pushes that don't change the
version, and pull requests, run steps 1 to 4 only.

## Building it yourself

Requirements: Windows 10/11, the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), and
[Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer (`winget install JRSoftware.InnoSetup`).

- Run from source: double-click `run.bat`, run `dotnet run --project src\CoinPilot.Desktop`, or open `CoinPilot.sln`
  in Visual Studio 2022 and press F5.
- Build everything: `.\build.ps1` makes `dist\CoinPilot.exe`, `dist\CoinPilot-<version>-win-x64.zip` and
  `dist\CoinPilot-<version>-Setup.exe`. Use `-SkipInstaller` to skip Inno Setup.

Command-line options: `--page <section>` opens a page at startup (for example `--page budgets` or `--page settings`),
and `--self-test <file>` runs the checks above and exits with the number of failures.

## Project layout

```
src/CoinPilot.Desktop/
  App.xaml(.cs)          app startup + light/dark theme switching
  MainWindow.xaml(.cs)   sidebar, page host, status bar, toast
  Assets/                app icon (.ico + .png)
  Themes/                Light.xaml, Dark.xaml (colors), Controls.xaml (styles)
  Data/                  Records (Transaction, Budget, Account, Debt, Bill, Goal), JsonStore (safe JSON files),
                         DataStore, Categories, Budgeting (month totals, budget progress, weekly spending),
                         Finance (account balances, debt payoff planner, bill schedule, goal pace, reports)
  Models/                ModuleCatalog.cs — every section, its planned features and columns
  Views/                 DashboardView, TransactionsView, BudgetsView (XAML); AccountsView, DebtsView, BillsView,
                         GoalsView, ReportsView (code, on DataPage + PageKit); ComingSoonView, SettingsView;
                         dialogs on DialogShell (TransactionDialog, BudgetDialog, MoneyDialogs.cs); Ui helpers
  Services/              money formatting, CSV export, settings, Windows theme detection, toast, self-test
installer/CoinPilot.iss  Inno Setup script (+ wizard images)
build.ps1                exe + portable zip + installer
.github/workflows/       CI: build, self-test, installer test, screenshots, release
design/mockup/           the desktop + mobile mockup this is built from
tools/make_icon.py       regenerates the icon (Python + Pillow)
tools/make_wizard_images.py  regenerates the installer wizard images
```

To add a coming-soon section, add an entry to `Models/ModuleCatalog.cs`.
To build a section for real, give it its own view in `Views/`, set `IsComingSoon = false` in the catalog and add it
to `MainWindow.Show`.

## Design

The look comes from the desktop and mobile mockup in [`design/mockup/`](design/mockup/)
(live mockup: [CoinPilot Mockup](https://claude.ai/artifact/4aCta5yZKNWszBcDLTvuvg), private until shared).

## Roadmap

1. ~~Transactions and Budgets, saved on this PC, which make the Dashboard real~~ (0.2.0)
2. ~~Accounts, Debts, Bills, Goals and Reports~~ (0.3.0)
3. Importing bank statements; your own categories; transfers between accounts; bill reminders
4. CoinPilot mobile app (iPhone and Android) and sync between devices

## License

MIT — see [LICENSE](LICENSE). © 2026 Bytehawk Studio.
