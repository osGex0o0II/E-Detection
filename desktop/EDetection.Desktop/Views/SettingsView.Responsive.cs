using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EDetection.Desktop.Views;

public sealed partial class SettingsView
{
    private void UpdateResponsiveLayout()
    {
        var availableWidth = SettingsRoot.ActualWidth;
        if (availableWidth <= 0)
        {
            return;
        }

        UpdateFooterLayout(availableWidth < StackedFooterBreakpoint);

        var contentWidth = SettingsScroll.ActualWidth;
        if (contentWidth <= 0)
        {
            return;
        }

        SettingsContentStack.Width = Math.Min(contentWidth, SettingsContentMaxWidth);

        var stackActions = contentWidth < StackedActionBreakpoint;
        foreach (var grid in GetResponsiveActionGrids())
        {
            ApplyActionGridLayout(grid, stackActions);
        }

        var stackThresholds = contentWidth < StackedThresholdBreakpoint;
        ApplyThresholdGridLayout(PrimaryThresholdGrid, stackThresholds);
        ApplyThresholdGridLayout(MoreThresholdGrid, stackThresholds);
    }

    private void UpdateFooterLayout(bool stack)
    {
        SettingsFooter.Padding = stack
            ? new Thickness(18, 10, 18, 10)
            : new Thickness(24, 12, 24, 12);

        Grid.SetColumn(SettingsFeedbackBar, 0);
        Grid.SetRow(SettingsFeedbackBar, 0);
        SettingsFeedbackBar.HorizontalAlignment = stack ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        SettingsFeedbackBar.MaxWidth = stack ? double.PositiveInfinity : 520;

        Grid.SetColumn(SettingsFooterActions, stack ? 0 : 1);
        Grid.SetRow(SettingsFooterActions, stack ? 1 : 0);
        SettingsFooterActions.HorizontalAlignment = HorizontalAlignment.Right;

        SettingsFooterLayout.ColumnSpacing = stack ? 0 : 16;
        SettingsFooterLayout.RowSpacing = stack ? 10 : 0;
        SettingsFooterLayout.ColumnDefinitions[1].Width = stack
            ? new GridLength(0)
            : GridLength.Auto;
    }

    private Grid[] GetResponsiveActionGrids() =>
    [
        DetectionParametersActionGrid,
        DesktopNotificationActionGrid,
        StartupActionGrid,
        ProxyActionGrid,
        UpdateActionGrid,
    ];

    private static void ApplyActionGridLayout(Grid grid, bool stack)
    {
        grid.RowDefinitions.Clear();
        if (grid.ColumnDefinitions.Count < 2 || grid.Children.Count < 2)
        {
            return;
        }

        grid.ColumnSpacing = stack ? 0 : 8;
        grid.RowSpacing = stack ? 8 : 0;
        grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        grid.ColumnDefinitions[1].Width = stack
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);

        var first = grid.Children[0] as FrameworkElement;
        var second = grid.Children[1] as FrameworkElement;
        if (first is null || second is null)
        {
            return;
        }

        Grid.SetColumn(first, 0);
        Grid.SetRow(first, 0);
        if (stack)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(second, 0);
            Grid.SetRow(second, 1);
            return;
        }

        Grid.SetColumn(second, 1);
        Grid.SetRow(second, 0);
    }

    private static void ApplyThresholdGridLayout(Grid grid, bool stack)
    {
        if (grid.ColumnDefinitions.Count < 2)
        {
            return;
        }

        grid.ColumnSpacing = stack ? 0 : 14;
        grid.RowSpacing = stack ? 10 : 12;
        grid.Padding = stack
            ? new Thickness(16, 12, 16, 12)
            : new Thickness(18, 12, 18, 12);
        grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        grid.ColumnDefinitions[1].Width = stack
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);

        var itemIndex = 0;
        foreach (var child in grid.Children.OfType<FrameworkElement>())
        {
            Grid.SetRow(child, stack ? itemIndex : itemIndex / 2);
            Grid.SetColumn(child, stack ? 0 : itemIndex % 2);
            Grid.SetColumnSpan(child, stack ? 2 : 1);
            itemIndex++;
        }

        var requiredRows = stack
            ? itemIndex
            : (int)Math.Ceiling(itemIndex / 2.0);
        while (grid.RowDefinitions.Count < requiredRows)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var i = 0; i < grid.RowDefinitions.Count; i++)
        {
            grid.RowDefinitions[i].Height = i < requiredRows
                ? GridLength.Auto
                : new GridLength(0);
        }
    }
}
