using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace EDetection.Desktop.Views;

public sealed partial class SettingsView
{
    public void NavigateToSectionByName(string sectionName)
    {
        SelectCategoryBySection(sectionName);
        NavigateToElementByName(sectionName);
    }

    private void SettingsCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCategoryScroll)
        {
            return;
        }

        if (SettingsCategoryList.SelectedItem is not ListViewItem { Tag: string sectionName }
            || SettingsScroll is null)
        {
            return;
        }

        NavigateToElementByName(sectionName);
    }

    private void SelectCategoryBySection(string sectionName)
    {
        var categorySectionName = GetCategorySectionName(sectionName);
        for (var i = 0; i < SettingsCategoryList.Items.Count; i++)
        {
            if (SettingsCategoryList.Items[i] is ListViewItem { Tag: string itemSection }
                && string.Equals(itemSection, categorySectionName, StringComparison.Ordinal))
            {
                _suppressCategoryScroll = true;
                SettingsCategoryList.SelectedIndex = i;
                _suppressCategoryScroll = false;
                return;
            }
        }
    }

    private void NavigateToElementByName(string elementName)
    {
        if (FindName(elementName) is not FrameworkElement element)
        {
            return;
        }

        _suppressScrollSync = true;
        var navigationVersion = ++_navigationScrollVersion;
        element.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = true,
            VerticalAlignmentRatio = 0,
        });
        _ = ResumeScrollSyncAfterNavigationAsync(navigationVersion);
    }

    private void ExpandSearchEntryContainers(SettingsSearchEntry entry)
    {
        if (entry.ExpandMoreThresholds)
        {
            MoreThresholdsExpander.IsExpanded = true;
        }

        if (!string.IsNullOrWhiteSpace(entry.ExpanderName)
            && FindName(entry.ExpanderName) is Expander expander)
        {
            expander.IsExpanded = true;
        }

        UpdateResponsiveLayout();
    }

    private void SettingsScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!_suppressScrollSync)
        {
            UpdateSelectedCategoryFromScroll();
        }
    }

    private void SettingsScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void SettingsBody_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void SettingsView_Loaded(object sender, RoutedEventArgs e)
    {
        CacheSettingRows();
        UpdateResponsiveLayout();
    }

    private void SettingsExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
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

    private async Task ResumeScrollSyncAfterNavigationAsync(int navigationVersion)
    {
        await Task.Delay(280);
        if (navigationVersion != _navigationScrollVersion)
        {
            return;
        }

        _suppressScrollSync = false;
        UpdateSelectedCategoryFromScroll();
    }

    private void UpdateSelectedCategoryFromScroll()
    {
        var activeSectionName = GetActiveCategorySectionName();
        if (string.IsNullOrWhiteSpace(activeSectionName))
        {
            return;
        }

        SelectCategoryBySection(activeSectionName);
    }

    private string? GetActiveCategorySectionName()
    {
        var sections = GetSettingSections();
        if (sections.Length == 0)
        {
            return null;
        }

        var activeIndex = 0;
        if (SettingsScroll.ScrollableHeight > 0
            && SettingsScroll.VerticalOffset >= SettingsScroll.ScrollableHeight - 1)
        {
            activeIndex = sections.Length - 1;
            return GetCategorySectionName(sections[activeIndex].Name);
        }

        var viewportHeight = SettingsScroll.ViewportHeight > 0
            ? SettingsScroll.ViewportHeight
            : SettingsScroll.ActualHeight;
        var activationY = Math.Clamp(viewportHeight * 0.34, 120, 300);

        for (var i = 0; i < sections.Length; i++)
        {
            var sectionTop = sections[i]
                .TransformToVisual(SettingsScroll)
                .TransformPoint(new Point(0, 0))
                .Y;

            if (sectionTop <= activationY)
            {
                activeIndex = i;
                continue;
            }

            break;
        }

        return GetCategorySectionName(sections[activeIndex].Name);
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
            "NotificationsSection" => "LlmSection",
            "UpdatesSection" => "ProxySection",
            "AdvancedSection" => "LlmSection",
            _ => sectionName,
        };
}
