using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

public partial class SettingsView : UserControl
{
    private readonly bool _ready;

    public SettingsView()
    {
        InitializeComponent();

        (App.Settings.Theme switch
        {
            AppTheme.Light => ThemeLight,
            AppTheme.Dark => ThemeDark,
            _ => ThemeSystem,
        }).IsChecked = true;

        VersionText.Text = $"CoinPilot {App.VersionText} (preview)";
        DataFolderText.Text = DataStore.Folder;
        _ready = true;
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_ready && sender is RadioButton { Tag: string mode })
            App.SetThemePreference(mode);
    }

    private void Soon_Click(object sender, RoutedEventArgs e) => Toaster.Soon(sender);

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DataStore.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataStore.Folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Toaster.Show($"Couldn't open the folder: {ex.Message}");
        }
    }
}
