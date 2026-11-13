using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>"‹ November 2026 ›" plus a "This month" link. Shared by Transactions and Budgets; the month is remembered across pages.</summary>
public sealed class MonthSwitcher : StackPanel
{
    /// <summary>The month the money pages are showing (first day of the month).</summary>
    public static DateTime Current { get; set; } = Budgeting.MonthStart(DateTime.Today);

    public event EventHandler? MonthChanged;

    private readonly TextBlock _label;
    private readonly Button _today;

    public MonthSwitcher()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;

        var prev = new Button { Content = "", Style = (Style)Application.Current.FindResource("IconButton"), ToolTip = "Previous month" };
        AutomationProperties.SetName(prev, "Previous month");
        prev.Click += (_, _) => Move(-1);

        _label = Ui.Text("", size: 15, weight: FontWeights.SemiBold);
        _label.Width = 150;
        _label.TextAlignment = TextAlignment.Center;

        var next = new Button { Content = "", Style = (Style)Application.Current.FindResource("IconButton"), ToolTip = "Next month" };
        AutomationProperties.SetName(next, "Next month");
        next.Click += (_, _) => Move(1);

        _today = new Button { Content = "This month", Style = (Style)Application.Current.FindResource("LinkButton"), Margin = new Thickness(6, 0, 0, 0) };
        _today.Click += (_, _) => Set(Budgeting.MonthStart(DateTime.Today));

        Children.Add(prev);
        Children.Add(_label);
        Children.Add(next);
        Children.Add(_today);
        UpdateLabel();
    }

    private void Move(int months) => Set(Current.AddMonths(months));

    private void Set(DateTime month)
    {
        Current = Budgeting.MonthStart(month);
        UpdateLabel();
        MonthChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateLabel()
    {
        _label.Text = Money.MonthName(Current);
        _today.Visibility = Current == Budgeting.MonthStart(DateTime.Today) ? Visibility.Hidden : Visibility.Visible;
    }
}
