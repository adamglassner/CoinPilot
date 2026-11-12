using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CoinPilot.Models;

namespace CoinPilot.Services;

/// <summary>
/// Headless checks run by CI: <c>CoinPilot.exe --self-test result.txt</c>.
/// Opens every page in both themes and checks the navigation. Exit code = number of failures.
/// </summary>
public static class SelfTest
{
    public static string? OutputFile { get; set; }
    public static bool IsRunning => OutputFile != null;

    private static readonly StringBuilder Report = new();
    private static int _failed;

    /// <summary>Called by App's unhandled-exception handler while the self-test runs.</summary>
    public static void RecordUnhandled(Exception ex)
    {
        _failed++;
        Report.AppendLine($"FAIL  Unhandled exception — {ex}");
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (!ok) _failed++;
        Report.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  — " + detail : "")}");
    }

    private static async Task Step(string name, Action action)
    {
        try
        {
            action();
            // Let layout, data binding and rendering run so XAML errors surface here.
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Application.Current.MainWindow?.UpdateLayout();
            Check(name, true);
        }
        catch (Exception ex)
        {
            Check(name, false, ex.ToString());
        }
    }

    public static async Task<int> RunAsync(MainWindow window)
    {
        Report.AppendLine($"CoinPilot {App.VersionText} self-test {DateTime.Now:u}");

        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            await Step($"[{theme}] Apply theme", () => App.ApplyTheme(theme));
            foreach (var module in ModuleCatalog.Sidebar)
            {
                await Step($"[{theme}] Page: {module.Title}", () => window.GoTo(module.Key));
                Check($"[{theme}] {module.Title} is selected in the sidebar",
                    module.IsSelected && ModuleCatalog.Sidebar.Count(m => m.IsSelected) == 1);
            }
            await Step($"[{theme}] Page: Settings", window.ShowSettings);
            Check($"[{theme}] Settings clears the sidebar selection", ModuleCatalog.Sidebar.All(m => !m.IsSelected));
            await Step($"[{theme}] Toast", () => window.ShowToast("Self-test toast"));
        }

        Check("Every section except the Dashboard is marked coming soon",
            ModuleCatalog.Sidebar.All(m => m.IsComingSoon == (m.Key != ModuleCatalog.Dashboard.Key)));
        Check("Every coming-soon section lists planned features",
            ModuleCatalog.Sidebar.Where(m => m.IsComingSoon).All(m => m.Features.Length > 0 && m.Description.Length > 0));
        Check("Section keys are unique",
            ModuleCatalog.Sidebar.Select(m => m.Key).Distinct().Count() == ModuleCatalog.Sidebar.Count);
        Check("Version is set", App.VersionText != "0.0.0", App.VersionText);

        await Step("Unknown page falls back to the Dashboard", () => window.GoTo("no-such-page"));
        Check("Dashboard selected after unknown page", ModuleCatalog.Dashboard.IsSelected);

        Report.AppendLine(_failed == 0 ? "ALL PASSED" : $"{_failed} FAILED");
        try { File.WriteAllText(OutputFile!, Report.ToString()); } catch { /* ignore */ }
        return _failed;
    }
}
