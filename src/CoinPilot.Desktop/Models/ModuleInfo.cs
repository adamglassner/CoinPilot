using System.ComponentModel;

namespace CoinPilot.Models;

/// <summary>Describes one section of the app. Drives the sidebar and the "coming soon" pages.</summary>
public sealed class ModuleInfo : INotifyPropertyChanged
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    /// <summary>Segoe Fluent / MDL2 icon glyph.</summary>
    public required string Glyph { get; init; }
    public string Tagline { get; init; } = "";
    public string Description { get; init; } = "";
    public string PrimaryAction { get; init; } = "Add";
    public string[] Features { get; init; } = [];
    public string[] Kpis { get; init; } = [];
    public string[] Columns { get; init; } = [];
    /// <summary>False for sections that work and have their own page (see MainWindow.Show).</summary>
    public bool IsComingSoon { get; init; } = true;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
