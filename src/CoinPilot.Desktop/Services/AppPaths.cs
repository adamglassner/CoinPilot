using System.IO;

namespace CoinPilot.Services;

/// <summary>
/// Where WillowWallet keeps its settings and data: %AppData%\WillowWallet. The app used to be called CoinPilot, so the
/// first time it runs after the rename it moves %AppData%\CoinPilot here (and keeps using the old folder if it can't).
/// </summary>
public static class AppPaths
{
    public static readonly string Root = Resolve();

    private static string Resolve()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string root = Path.Combine(appData, "WillowWallet");
        string old = Path.Combine(appData, "CoinPilot");
        if (!Directory.Exists(root) && Directory.Exists(old))
        {
            try { Directory.Move(old, root); }
            catch (Exception) { return old; } // in use or no permission: keep working from the old folder
        }
        return root;
    }
}
