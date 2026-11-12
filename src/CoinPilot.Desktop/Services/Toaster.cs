using System.Windows;

namespace CoinPilot.Services;

/// <summary>Shows the little "coming soon" message for every not-yet-built feature.</summary>
public static class Toaster
{
    public static void Show(string message) => MainWindow.Instance?.ShowToast(message);

    /// <summary>Uses the sender's Tag (if a string) as the feature name.</summary>
    public static void Soon(object? sender)
    {
        string what = (sender as FrameworkElement)?.Tag as string ?? "This feature";
        Show($"{what} is coming soon.");
    }
}
