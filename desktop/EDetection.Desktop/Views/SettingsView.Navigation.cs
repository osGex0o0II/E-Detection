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

        ShowCategory(sectionName);
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
                ShowCategory(categorySectionName);
                return;
            }
        }
    }

    private void ShowCategory(string categorySectionName)
    {
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

        if (root is Control rootControl && TryFocus(rootControl))
        {
            return;
        }

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

        SettingsCategoryList.Focus(FocusState.Programmatic);
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
            && FindName(entry.ExpanderName) is Expander expander)
        {
            expander.IsExpanded = true;
        }

        UpdateResponsiveLayout();
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
        SelectCategoryBySection("DefaultsSection");
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
