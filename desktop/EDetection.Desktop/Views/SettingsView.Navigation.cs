using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace EDetection.Desktop.Views;

public sealed partial class SettingsView
{
    public void NavigateToSectionByName(string sectionName)
    {
        SelectCategoryBySection(sectionName);
        NavigateToElementByName(sectionName);
    }

    private void SettingsNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (_suppressCategoryScroll)
        {
            return;
        }

        if (sender.SelectedItem is not NavigationViewItem { Tag: string sectionName }
            || SettingsScroll is null)
        {
            return;
        }

        ShowCategory(sectionName);
    }

    private void SelectCategoryBySection(string sectionName)
    {
        var categorySectionName = GetCategorySectionName(sectionName);
        foreach (var item in SettingsNavigation.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag is string itemSection
                && string.Equals(itemSection, categorySectionName, StringComparison.Ordinal))
            {
                _suppressCategoryScroll = true;
                SettingsNavigation.SelectedItem = item;
                _suppressCategoryScroll = false;
                ShowCategory(categorySectionName);
                return;
            }
        }
    }

    private void ShowCategory(string categorySectionName)
    {
        _lastCategorySectionName = categorySectionName;

        // Footer feedback belongs to the category where the action ran; leaving
        // it open while navigating makes the new category look like it failed.
        SettingsFeedbackBar.IsOpen = false;

        foreach (var section in GetSettingSections())
        {
            section.Visibility = GetCategorySectionName(section.Name) == categorySectionName
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        var showDetectionActions = categorySectionName == "DefaultsSection";
        SaveDetectionSettingsButton.Visibility = showDetectionActions ? Visibility.Visible : Visibility.Collapsed;
        SaveAsDetectionConfigButton.Visibility = showDetectionActions ? Visibility.Visible : Visibility.Collapsed;
        SettingsScroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void FocusSearchTarget(string elementName)
    {
        if (FindName(elementName) is not DependencyObject root)
        {
            return;
        }

        // Descendants first, then the container itself.  A SettingsCard is a
        // ContentControl and therefore focusable, so focusing the root first
        // would park focus on the card shell instead of the editor inside it
        // (the pre-migration rows were plain Grids, which are not focusable,
        // so the BFS always reached the real control).
        var pending = new Queue<DependencyObject>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!ReferenceEquals(current, root) && current is Control control && TryFocus(control))
            {
                return;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < childCount; i++)
            {
                pending.Enqueue(VisualTreeHelper.GetChild(current, i));
            }
        }

        if (root is Control rootControl && TryFocus(rootControl))
        {
            return;
        }

        SettingsSearchBox.Focus(FocusState.Programmatic);
    }

    private static bool TryFocus(Control control) =>
        control.IsEnabled
        && control.Visibility == Visibility.Visible
        && control.Focus(FocusState.Programmatic);

    private void NavigateToElementByName(string elementName)
    {
        if (FindName(elementName) is not FrameworkElement element)
        {
            return;
        }

        element.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled,
            VerticalAlignmentRatio = 0,
        });
    }

    private void ExpandSearchEntryContainers(SettingsSearchEntry entry)
    {
        if (entry.ExpandMoreThresholds)
        {
            MoreThresholdsExpander.IsExpanded = true;
        }

        if (!string.IsNullOrWhiteSpace(entry.ExpanderName)
            && FindName(entry.ExpanderName) is CommunityToolkit.WinUI.Controls.SettingsExpander expander)
        {
            expander.IsExpanded = true;
        }

        UpdateResponsiveLayout();
    }

    private void SettingsScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void SettingsNavigation_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncSettingsSearchHost();
        UpdateResponsiveLayout();
    }

    private void SettingsNavigation_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        SyncSettingsSearchHost();
        UpdateResponsiveLayout();
    }

    private void SettingsView_Loaded(object sender, RoutedEventArgs e)
    {
        SyncSettingsSearchHost();
        SelectCategoryBySection("DefaultsSection");
        UpdateResponsiveLayout();
    }

    /// <summary>
    /// Keeps the settings search reachable in both pane layouts.  The pane
    /// header is hidden once NavigationView collapses to the 56 DIP icon rail,
    /// so the same panel moves to a host above the content instead of being
    /// hidden with it.
    /// </summary>
    private void SyncSettingsSearchHost()
    {
        if (SettingsNavigation is null || SettingsSearchPanel is null || SettingsCompactSearchHost is null)
        {
            return;
        }

        var compact = SettingsNavigation.DisplayMode != NavigationViewDisplayMode.Expanded;
        var hostedInContent = SettingsCompactSearchHost.Content is not null;
        if (compact == hostedInContent)
        {
            return;
        }

        if (compact)
        {
            SettingsNavigation.PaneHeader = null;
            SettingsCompactSearchHost.Content = SettingsSearchPanel;
            SettingsCompactSearchHost.Visibility = Visibility.Visible;
        }
        else
        {
            SettingsCompactSearchHost.Content = null;
            SettingsCompactSearchHost.Visibility = Visibility.Collapsed;
            SettingsNavigation.PaneHeader = SettingsSearchPanel;
        }
    }

    private void SettingsExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        QueueResponsiveLayoutRefresh(sender);
    }

    /// <summary>
    /// Expanding an expander reveals grids whose columns are rebuilt by
    /// <see cref="UpdateResponsiveLayout"/>; the layout must be re-measured once
    /// the content has been measured, and once more after that pass has
    /// applied, hence the nested dispatch.</summary>
    private void QueueResponsiveLayoutRefresh(FrameworkElement sender)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateResponsiveLayout();
            DispatcherQueue.TryEnqueue(() =>
            {
                sender.UpdateLayout();
                UpdateResponsiveLayout();
            });
        });
    }

    private FrameworkElement[] GetSettingSections() =>
    [
        DefaultsSection,
        ThresholdsSection,
        DetectionRulesSection,
        ReportsSection,
        LogsSection,
        WindowSection,
        LlmSection,
        NotificationsSection,
        AdvancedSection,
        ProxySection,
        UpdatesSection,
        AppearanceSection,
    ];

    private static string GetCategorySectionName(string sectionName) =>
        sectionName switch
        {
            "ThresholdsSection" or "DetectionRulesSection" => "DefaultsSection",
            "LogsSection" => "ReportsSection",
            "AppearanceSection" => "WindowSection",
            "ProxySection" => "LlmSection",
            "AdvancedSection" => "UpdatesSection",
            _ => sectionName,
        };
}
