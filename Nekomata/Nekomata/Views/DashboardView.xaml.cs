using System.Windows;
using System.Windows.Controls;

namespace Nekomata.UI.Views;

public partial class DashboardView : UserControl
{
    private const double CompactBreakpoint = 1100;

    public DashboardView()
    {
        InitializeComponent();

        Loaded += (_, _) =>
            ApplyResponsiveLayout(ActualWidth);
    }

    private void DashboardView_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width);
    }

    private void ApplyResponsiveLayout(double availableWidth)
    {
        var compact =
            availableWidth < CompactBreakpoint;

        if (compact)
        {
            ApplyCompactLayout();
        }
        else
        {
            ApplyWideLayout();
        }
    }

    private void ApplyCompactLayout()
    {
        LeftColumn.Width =
            new GridLength(1, GridUnitType.Star);

        ColumnGap.Width =
            new GridLength(0);

        RightColumn.Width =
            new GridLength(0);

        // Mission and today's work remain immediately below briefing.
        Grid.SetRow(LeftDashboardPanel, 2);
        Grid.SetColumn(LeftDashboardPanel, 0);
        Grid.SetColumnSpan(LeftDashboardPanel, 3);

        // Guardian, productivity and portfolio move underneath.
        Grid.SetRow(RightDashboardPanel, 3);
        Grid.SetColumn(RightDashboardPanel, 0);
        Grid.SetColumnSpan(RightDashboardPanel, 3);

        RightDashboardPanel.Margin =
            new Thickness(0, 18, 0, 0);
    }

    private void ApplyWideLayout()
    {
        LeftColumn.Width =
            new GridLength(1.08, GridUnitType.Star);

        ColumnGap.Width =
            new GridLength(20);

        RightColumn.Width =
            new GridLength(0.92, GridUnitType.Star);

        // Both panels sit side-by-side beneath briefing.
        Grid.SetRow(LeftDashboardPanel, 2);
        Grid.SetColumn(LeftDashboardPanel, 0);
        Grid.SetColumnSpan(LeftDashboardPanel, 1);

        Grid.SetRow(RightDashboardPanel, 2);
        Grid.SetColumn(RightDashboardPanel, 2);
        Grid.SetColumnSpan(RightDashboardPanel, 1);

        RightDashboardPanel.Margin =
            new Thickness(0);
    }
}