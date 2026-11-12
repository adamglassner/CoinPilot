# CoinPilot

<img src="assets/coinpilot-256.png" width="96" align="right" alt="CoinPilot icon" />

**A budgeting app that keeps your budgets, spending and goals in sync between your PC and your phone.**
Made by Bytehawk Studio.

> **Status: early preview (v0.1.1), Windows only.** This is the app's shell: the window, navigation,
> light and dark mode and settings work, the Dashboard shows the planned layout with sample data, and every
> other section is a "coming soon" page describing what it will do. Nothing is tracked or saved yet.

## What's in the preview

| Section | Status |
| --- | --- |
| **Dashboard** | Preview with sample data: left to spend, spent, income, savings, budget progress, spending by week and recent transactions. |
| Transactions | Coming soon |
| Budgets | Coming soon |
| Goals | Coming soon |
| Accounts | Coming soon |
| Bills | Coming soon |
| Reports | Coming soon |
| Sync & Devices | Coming soon (sync with the CoinPilot mobile app) |
| Appearance | Works: light / dark mode from the sidebar, or Settings → Appearance → "Match Windows". |

Each coming-soon page lists its planned features. They are defined in
[`Models/ModuleCatalog.cs`](src/CoinPilot.Desktop/Models/ModuleCatalog.cs).

## Download

Windows 10/11 (x64). Install it from the **[Bytehawk Studio Launcher](https://github.com/adamglassner/bytehawklauncher)**,
which downloads the newest release, installs it and keeps it up to date. Or get it from the
[latest release](../../releases/latest):

- **`CoinPilot-<version>-Setup.exe`**: installer with Start Menu shortcut, optional desktop shortcut and an
  uninstaller. Installs just for you by default (no administrator prompt).
- **`CoinPilot-<version>-win-x64.zip`**: portable. Unzip anywhere and run `CoinPilot.exe`.

Neither needs .NET installed. Settings are saved in `%AppData%\CoinPilot\`.

## Releasing a new version

Change `<Version>` in `src/CoinPilot.Desktop/CoinPilot.Desktop.csproj` (for example to `0.1.1`), add a
[CHANGELOG](CHANGELOG.md) entry and push to `main`. GitHub Actions ([build-release.yml](.github/workflows/build-release.yml))
then, on a Windows machine:

1. builds the single-file exe, the portable zip and the installer,
2. runs the self-test (`CoinPilot.exe --self-test result.txt`): every page in light and dark, plus navigation checks,
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
  Models/                ModuleCatalog.cs — every section, its planned features and columns
  Views/                 DashboardView (sample-data preview), ComingSoonView, SettingsView
  Services/              settings, Windows theme detection, toast helper, self-test
installer/CoinPilot.iss  Inno Setup script (+ wizard images)
build.ps1                exe + portable zip + installer
.github/workflows/       CI: build, self-test, installer test, screenshots, release
design/mockup/           the desktop + mobile mockup this is built from
tools/make_icon.py       regenerates the icon (Python + Pillow)
tools/make_wizard_images.py  regenerates the installer wizard images
```

To add a coming-soon section, add an entry to `Models/ModuleCatalog.cs`.
To build a section for real, give it its own view in `Views/`, set `IsComingSoon = false` and show that view
from `MainWindow.Show`.

## Design

The look comes from the desktop and mobile mockup in [`design/mockup/`](design/mockup/)
(live mockup: [CoinPilot Mockup](https://claude.ai/artifact/4aCta5yZKNWszBcDLTvuvg), private until shared).

## Roadmap

1. Transactions and Budgets, saved on this PC, which make the Dashboard real
2. Accounts, Goals and Bills
3. Reports
4. CoinPilot mobile app (iPhone and Android) and sync between devices

## License

MIT — see [LICENSE](LICENSE). © 2026 Bytehawk Studio.
