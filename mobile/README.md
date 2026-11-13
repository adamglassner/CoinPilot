# WillowWallet for Android

Budgets and spending on your phone. Flutter, Android 7+ (API 24). Made by Bytehawk Studio. Formerly CoinPilot.

| Screen | What it does |
| --- | --- |
| **Home** | Left to spend this month, income and net, the budgets closest to their limit, spending by week, recent transactions. A welcome card (add a transaction, set up a budget, or explore with sample data) when it's empty. |
| **Transactions** | Month by month with spent / income / net, search, All / Spending / Income, grouped by day. Tap to edit, swipe left to delete (with Undo). |
| **Budgets** | A monthly limit per category with progress (amber near the limit, red over), totals, and spending that isn't budgeted yet ("Budget it"). New budgets suggest an amount from your last three months. |
| **Settings** | Auto / Light / Dark, the eight color themes from the Windows app, currency, sample data, erase everything. |

The **+** button adds spending or income from anywhere. WillowWallet remembers the category you use for each payee.

## Your data

One JSON file in the app's private storage (`coinpilot.json`), written safely (temp file then swap). It uses the
Windows app's field names (`Payee`, `Category`, `Account`, `Date`, `Amount`, `MonthlyLimit`…) so syncing between
the two can be added later. Uninstalling the app deletes it.

## Install

Download `WillowWallet-Android-<version>.apk` from the newest **android-v…** release on your phone and open it. Android
asks you to allow installs from your browser or Files app the first time.

## Building

CI ([android.yml](../.github/workflows/android.yml)) does everything on Ubuntu: `flutter create` adds the Android
project files (only `lib/`, `test/`, `assets/` and `android-res/` are in git), then analyze, tests, screenshots
(`flutter test --update-goldens test/screenshots_test.dart`, published to the `screenshots-android` branch), the
release APK, signing, and on `main` a release `android-v<version>` when the version in `pubspec.yaml` is new.
The release is never marked "latest", so the Bytehawk Studio Launcher keeps installing the Windows app.

Signing uses two repository secrets: `ANDROID_KEYSTORE_B64` (the .jks file, base64) and `ANDROID_KEYSTORE_PASSWORD`
(alias `coinpilot`). Without them the APK keeps a throwaway signature, and installing a properly signed version over
it later needs an uninstall first.

Locally: `cd mobile && flutter create --platforms=android --org com.bytehawkstudio --project-name coinpilot .`,
copy `android-res/` into `android/app/src/main/res/`, then `flutter run`.

Fonts: Instrument Sans and Bricolage Grotesque (SIL Open Font License, in `assets/fonts`).
