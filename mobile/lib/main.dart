import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'screens/budgets_screen.dart';
import 'screens/home_screen.dart';
import 'screens/settings_screen.dart';
import 'screens/transaction_sheet.dart';
import 'screens/transactions_screen.dart';
import 'store.dart';
import 'theme.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge);
  final store = await Store.open();
  runApp(CoinPilotApp(store: store));
}

class CoinPilotApp extends StatelessWidget {
  const CoinPilotApp({super.key, required this.store, this.initialTab = 0});
  final Store store;
  final int initialTab;

  @override
  Widget build(BuildContext context) {
    return StoreScope(
      store: store,
      child: ListenableBuilder(
        listenable: store,
        builder: (context, _) {
          final s = store.settings;
          return MaterialApp(
            title: 'WillowWallet',
            debugShowCheckedModeBanner: false,
            theme: buildTheme(Palette.build(s.colorTheme, false)),
            darkTheme: buildTheme(Palette.build(s.colorTheme, true)),
            themeMode: switch (s.themeMode) {
              'light' => ThemeMode.light,
              'dark' => ThemeMode.dark,
              _ => ThemeMode.system,
            },
            home: AppShell(initialTab: initialTab),
          );
        },
      ),
    );
  }
}

/// Bottom navigation (Home, Transactions, Budgets, Settings) around the add button.
class AppShell extends StatefulWidget {
  const AppShell({super.key, this.initialTab = 0});
  final int initialTab;

  @override
  State<AppShell> createState() => _AppShellState();
}

class _AppShellState extends State<AppShell> {
  late int _tab = widget.initialTab;

  void _go(int tab) => setState(() => _tab = tab);

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final overlay = (p.dark ? SystemUiOverlayStyle.light : SystemUiOverlayStyle.dark).copyWith(
      statusBarColor: Colors.transparent,
      systemNavigationBarColor: p.surface,
      systemNavigationBarIconBrightness: p.dark ? Brightness.light : Brightness.dark,
    );
    Widget item(int i, IconData icon, IconData selectedIcon, String label) {
      final on = _tab == i;
      return Expanded(
        child: Semantics(
          selected: on,
          button: true,
          label: label,
          excludeSemantics: true,
          child: InkResponse(
            onTap: () => _go(i),
            radius: 40,
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                AnimatedContainer(
                  duration: const Duration(milliseconds: 200),
                  padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
                  decoration: BoxDecoration(
                    color: on ? p.accentSoft : Colors.transparent,
                    borderRadius: BorderRadius.circular(99),
                  ),
                  child: Icon(on ? selectedIcon : icon, size: 23, color: on ? p.accent : p.muted),
                ),
                const SizedBox(height: 4),
                Text(label,
                    style: TextStyle(
                        fontSize: 11.5, fontWeight: on ? FontWeight.w700 : FontWeight.w500, color: on ? p.text : p.muted)),
              ],
            ),
          ),
        ),
      );
    }

    return AnnotatedRegion<SystemUiOverlayStyle>(
      value: overlay,
      child: Scaffold(
        body: IndexedStack(
          index: _tab,
          children: [
            HomeScreen(onNavigate: _go),
            const TransactionsScreen(),
            const BudgetsScreen(),
            const SettingsScreen(),
          ],
        ),
        floatingActionButton: FloatingActionButton(
          tooltip: 'Add transaction',
          onPressed: () => showTransactionSheet(context),
          backgroundColor: p.accent,
          foregroundColor: p.onAccent,
          elevation: 3,
          highlightElevation: 6,
          shape: const CircleBorder(),
          child: const Icon(Icons.add_rounded, size: 32),
        ),
        floatingActionButtonLocation: FloatingActionButtonLocation.centerDocked,
        bottomNavigationBar: BottomAppBar(
          color: p.surface,
          surfaceTintColor: Colors.transparent,
          shadowColor: Colors.black,
          elevation: 10,
          shape: const CircularNotchedRectangle(),
          notchMargin: 7,
          height: 74,
          padding: EdgeInsets.zero,
          child: Row(
            children: [
              item(0, Icons.home_outlined, Icons.home_rounded, 'Home'),
              item(1, Icons.receipt_long_outlined, Icons.receipt_long_rounded, 'Transactions'),
              const SizedBox(width: 76),
              item(2, Icons.pie_chart_outline_rounded, Icons.pie_chart_rounded, 'Budgets'),
              item(3, Icons.settings_outlined, Icons.settings_rounded, 'Settings'),
            ],
          ),
        ),
      ),
    );
  }
}
