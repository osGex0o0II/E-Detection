using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EDetection.Desktop.Views;

public sealed partial class SettingsView
{
    private void CacheSettingRows()
    {
        CollectSettingRows(SettingsContentStack);
    }

    private void CollectSettingRows(DependencyObject root)
    {
        var childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Grid grid
                && !_settingRows.Any(row => ReferenceEquals(row.Row, grid))
                && TryCreateSettingRowState(grid, out var rowState))
            {
                _settingRows.Add(rowState);
            }

            CollectSettingRows(child);
        }
    }

    private static bool TryCreateSettingRowState(Grid grid, out SettingRowLayoutState rowState)
    {
        rowState = default!;
        if (grid.MinHeight < 64 || grid.ColumnDefinitions.Count != 2 || grid.RowDefinitions.Count > 0)
        {
            return false;
        }

        var rowChildren = grid.Children.OfType<FrameworkElement>().ToArray();
        if (rowChildren.Length != 2
            || Grid.GetColumn(rowChildren[0]) != 0
            || Grid.GetColumn(rowChildren[1]) != 1)
        {
            return false;
        }

        rowState = SettingRowLayoutState.Create(grid, rowChildren[0], rowChildren[1]);
        return true;
    }

    private void UpdateResponsiveLayout()
    {
        CacheSettingRows();
        var availableWidth = SettingsBody.ActualWidth;
        if (availableWidth <= 0)
        {
            return;
        }

        var compact = availableWidth < CompactBodyBreakpoint;
        var iconOnlyNavigation = availableWidth < IconNavigationBreakpoint;
        SettingsBody.Padding = compact
            ? new Thickness(18, 18, 18, 0)
            : new Thickness(24, 22, 24, 0);
        SettingsBody.ColumnSpacing = iconOnlyNavigation ? 14 : compact ? 18 : 24;
        SettingsNavColumn.Width = new GridLength(iconOnlyNavigation
            ? IconNavigationWidth
            : compact ? CompactNavigationWidth : ExpandedNavigationWidth);
        UpdateNavigationRailLayout(iconOnlyNavigation);
        UpdateFooterLayout(availableWidth < StackedFooterBreakpoint);

        var contentWidth = SettingsScroll.ActualWidth;
        if (contentWidth <= 0)
        {
            return;
        }

        SettingsContentStack.Width = Math.Min(contentWidth, SettingsContentMaxWidth);
        SettingsContentStack.MaxWidth = SettingsContentMaxWidth;

        var stackSettingRows = contentWidth < StackedSettingsRowsBreakpoint;
        var labelWidth = contentWidth < CompactContentBreakpoint ? CompactLabelWidth : ExpandedLabelWidth;
        foreach (var row in _settingRows)
        {
            ApplySettingsRowLayout(row, stackSettingRows, labelWidth);
        }

        var stackActions = contentWidth < StackedActionBreakpoint;
        foreach (var grid in GetResponsiveActionGrids())
        {
            ApplyActionGridLayout(grid, stackActions);
        }

        var stackThresholds = contentWidth < StackedThresholdBreakpoint;
        ApplyThresholdGridLayout(PrimaryThresholdGrid, stackThresholds);
        ApplyThresholdGridLayout(MoreThresholdGrid, stackThresholds);
    }

    private void UpdateNavigationRailLayout(bool iconOnly)
    {
        SettingsSearchBox.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
        SettingsSearchBox.Width = iconOnly ? IconNavigationWidth : double.NaN;
        SettingsSearchBox.PlaceholderText = iconOnly ? "" : "搜索设置";
        SettingsNavRail.RowSpacing = iconOnly ? 0 : 10;

        foreach (var item in SettingsCategoryList.Items.OfType<ListViewItem>())
        {
            var title = FindFirstTextBlockText(item.Content as DependencyObject);
            if (!string.IsNullOrWhiteSpace(title))
            {
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, title);
                ToolTipService.SetToolTip(item, title);
            }

            item.HorizontalContentAlignment = iconOnly
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Stretch;
            item.Padding = iconOnly ? new Thickness(0) : new Thickness(12, 0, 12, 0);
            item.MinHeight = iconOnly ? 44 : 40;

            if (item.Content is DependencyObject content)
            {
                UpdateNavigationItemContent(content, iconOnly);
            }
        }
    }

    private static void UpdateNavigationItemContent(DependencyObject root, bool iconOnly)
    {
        if (root is StackPanel stack)
        {
            stack.Spacing = iconOnly ? 0 : 10;
            stack.HorizontalAlignment = iconOnly ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        }

        if (root is TextBlock textBlock)
        {
            textBlock.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
        }

        var childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            UpdateNavigationItemContent(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i), iconOnly);
        }
    }

    private static string? FindFirstTextBlockText(DependencyObject? root)
    {
        if (root is null)
        {
            return null;
        }

        if (root is TextBlock textBlock)
        {
            return textBlock.Text;
        }

        var childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var text = FindFirstTextBlockText(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i));
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
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

    private static void ApplySettingsRowLayout(SettingRowLayoutState state, bool stack, double labelWidth)
    {
        var row = state.Row;
        row.RowDefinitions.Clear();

        if (!stack)
        {
            state.Restore(labelWidth);
            return;
        }

        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.ColumnSpacing = 0;
        row.RowSpacing = 8;
        row.Padding = new Thickness(16, 12, 16, 12);
        state.LabelColumn.MinWidth = 0;
        state.LabelColumn.MaxWidth = double.PositiveInfinity;
        state.LabelColumn.Width = new GridLength(1, GridUnitType.Star);
        state.ControlColumn.MinWidth = 0;
        state.ControlColumn.MaxWidth = double.PositiveInfinity;
        state.ControlColumn.Width = new GridLength(0);

        Grid.SetRow(state.Label, 0);
        Grid.SetColumn(state.Label, 0);
        Grid.SetColumnSpan(state.Label, 2);
        Grid.SetRow(state.Control, 1);
        Grid.SetColumn(state.Control, 0);
        Grid.SetColumnSpan(state.Control, 2);
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

    private sealed record SettingRowLayoutState(
        Grid Row,
        FrameworkElement Label,
        FrameworkElement Control,
        ColumnDefinition LabelColumn,
        ColumnDefinition ControlColumn,
        GridLength OriginalLabelColumnWidth,
        GridLength OriginalControlColumnWidth,
        double OriginalLabelColumnMaxWidth,
        double OriginalControlColumnMaxWidth,
        double OriginalLabelColumnMinWidth,
        double OriginalControlColumnMinWidth,
        Thickness OriginalPadding,
        double OriginalColumnSpacing,
        double OriginalRowSpacing,
        int OriginalLabelRow,
        int OriginalLabelColumn,
        int OriginalLabelColumnSpan,
        int OriginalControlRow,
        int OriginalControlColumn,
        int OriginalControlColumnSpan)
    {
        public static SettingRowLayoutState Create(Grid row, FrameworkElement label, FrameworkElement control) =>
            new(
                row,
                label,
                control,
                row.ColumnDefinitions[0],
                row.ColumnDefinitions[1],
                row.ColumnDefinitions[0].Width,
                row.ColumnDefinitions[1].Width,
                row.ColumnDefinitions[0].MaxWidth,
                row.ColumnDefinitions[1].MaxWidth,
                row.ColumnDefinitions[0].MinWidth,
                row.ColumnDefinitions[1].MinWidth,
                row.Padding,
                row.ColumnSpacing,
                row.RowSpacing,
                Grid.GetRow(label),
                Grid.GetColumn(label),
                Grid.GetColumnSpan(label),
                Grid.GetRow(control),
                Grid.GetColumn(control),
                Grid.GetColumnSpan(control));

        public void Restore(double labelWidth)
        {
            Row.ColumnSpacing = OriginalColumnSpacing;
            Row.RowSpacing = OriginalRowSpacing;
            Row.Padding = OriginalPadding;

            LabelColumn.MinWidth = OriginalLabelColumnMinWidth;
            LabelColumn.MaxWidth = OriginalLabelColumnMaxWidth;
            LabelColumn.Width = OriginalLabelColumnWidth.IsAbsolute
                ? new GridLength(labelWidth)
                : OriginalLabelColumnWidth;
            ControlColumn.MinWidth = OriginalControlColumnMinWidth;
            ControlColumn.MaxWidth = OriginalControlColumnMaxWidth;
            ControlColumn.Width = OriginalControlColumnWidth;

            Grid.SetRow(Label, OriginalLabelRow);
            Grid.SetColumn(Label, OriginalLabelColumn);
            Grid.SetColumnSpan(Label, OriginalLabelColumnSpan);
            Grid.SetRow(Control, OriginalControlRow);
            Grid.SetColumn(Control, OriginalControlColumn);
            Grid.SetColumnSpan(Control, OriginalControlColumnSpan);
        }
    }
}
