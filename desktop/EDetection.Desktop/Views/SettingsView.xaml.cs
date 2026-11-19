using EDetection.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.UI.ViewManagement;

namespace EDetection.Desktop.Views;

public sealed partial class SettingsView : UserControl
{
    private const double IconNavigationWidth = 56;
    private const double SettingsContentMaxWidth = 900;
    private const double CompactBodyBreakpoint = 1040;
    private const double IconNavigationBreakpoint = 820;
    private const double CompactContentBreakpoint = 720;
    private const double StackedActionBreakpoint = CompactContentBreakpoint;
    private const double StackedFooterBreakpoint = 820;
    private const double StackedThresholdBreakpoint = 640;

    private bool _suppressCategoryScroll;
    private string _lastCategorySectionName = "DefaultsSection";
    private readonly IReadOnlyList<SettingsSearchEntry> _searchEntries;

    public SettingsView()
    {
        InitializeComponent();
        _searchEntries = BuildSearchEntries();
        _suppressCategoryScroll = true;
        SettingsCategoryList.SelectedIndex = 0;
        _suppressCategoryScroll = false;
        Loaded += SettingsView_Loaded;
    }

    public event EventHandler? BackRequested;

    public event EventHandler? AboutRequested;

    public void FocusSearchOrNavigation()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (SettingsSearchBox.Visibility == Visibility.Visible)
            {
                SettingsSearchBox.Focus(FocusState.Programmatic);
            }
            else
            {
                SettingsCategoryList.Focus(FocusState.Programmatic);
            }
        });
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e) =>
        AboutRequested?.Invoke(this, EventArgs.Empty);

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    public void NavigateToThresholdSettings()
    {
        NavigateToSectionByName("ThresholdsSection");
    }

    public void NavigateToDetectionRules()
    {
        NavigateToSectionByName("DetectionRulesSection");
    }

    public void PrepareForDisplay(string? initialSectionName = null)
    {
        // Reopening settings used to snap back to the first category and scroll
        // top, which lost the user's place.  Keep the last category unless a
        // caller explicitly asks for a section.
        var targetSection = string.IsNullOrWhiteSpace(initialSectionName)
            ? _lastCategorySectionName
            : initialSectionName;
        _suppressCategoryScroll = true;
        SettingsCategoryList.SelectedIndex = 0;
        _suppressCategoryScroll = false;
        SelectCategoryBySection(GetCategorySectionName(targetSection));
        SettingsScroll.ChangeView(null, 0, null, disableAnimation: true);
        DispatcherQueue.TryEnqueue(() =>
        {
            SettingsScroll.UpdateLayout();
            SettingsScroll.ChangeView(null, 0, null, disableAnimation: true);
            NavigateToSectionByName(targetSection);
        });
    }

    public void PlayEntranceAnimation(bool forward)
    {
        if (!new UISettings().AnimationsEnabled)
        {
            SettingsRoot.Opacity = 1;
            SettingsRootTransform.TranslateX = 0;
            return;
        }

        SettingsRoot.Opacity = 0;
        SettingsRootTransform.TranslateX = forward ? 28 : -28;

        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var opacityAnimation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = 1,
            Duration = TimeSpan.FromMilliseconds(180),
            EnableDependentAnimation = true,
        };
        var translateAnimation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(220),
            EnableDependentAnimation = true,
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut,
            },
        };

        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(opacityAnimation, SettingsRoot);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(opacityAnimation, "Opacity");
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(translateAnimation, SettingsRootTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(translateAnimation, "TranslateX");
        storyboard.Children.Add(opacityAnimation);
        storyboard.Children.Add(translateAnimation);
        storyboard.Begin();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private void SettingsRoot_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape || Visibility != Visibility.Visible)
        {
            return;
        }

        // Escape belongs to the topmost layer first.  A ComboBox dropdown or an
        // AutoSuggestBox suggestion list lives in its own popup, so closing the
        // whole page here would discard the user's place and break the Windows
        // convention that Escape dismisses the innermost surface.
        if (HasOpenPopup())
        {
            return;
        }

        // PreviewKeyDown tunnels through the page before controls such as
        // AutoSuggestBox, TextBox, and ComboBox consume Escape.  A keyboard
        // accelerator on the root only fired when focus was outside those
        // controls and also rendered a stray accelerator tooltip.
        e.Handled = true;
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool HasOpenPopup()
    {
        var xamlRoot = XamlRoot;
        if (xamlRoot is null)
        {
            return false;
        }

        foreach (var popup in Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
        {
            // Tooltips are popups too, but Escape must not be captured just
            // because a tooltip happens to be visible.
            if (popup.Child is Microsoft.UI.Xaml.Controls.ToolTip)
            {
                continue;
            }

            if (popup.Child is FrameworkElement { Visibility: Visibility.Visible })
            {
                return true;
            }
        }

        return false;
    }

    private void OpenThresholdSettings_Click(object sender, RoutedEventArgs e) =>
        NavigateToThresholdSettings();

    private void OpenDetectionRules_Click(object sender, RoutedEventArgs e) =>
        NavigateToDetectionRules();
}
