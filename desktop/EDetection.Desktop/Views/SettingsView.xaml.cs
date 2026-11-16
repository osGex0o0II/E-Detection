using EDetection.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

namespace EDetection.Desktop.Views;

public sealed partial class SettingsView : UserControl
{
    private const double IconNavigationWidth = 56;
    private const double ExpandedLabelWidth = 200;
    private const double CompactLabelWidth = 168;
    private const double SettingsContentMaxWidth = 900;
    private const double CompactBodyBreakpoint = 1040;
    private const double IconNavigationBreakpoint = 820;
    private const double CompactContentBreakpoint = 720;
    private const double StackedSettingsRowsBreakpoint = 780;
    private const double StackedActionBreakpoint = CompactContentBreakpoint;
    private const double StackedFooterBreakpoint = 820;
    private const double StackedThresholdBreakpoint = 640;

    private bool _suppressCategoryScroll;
    private bool _suppressScrollSync;
    private int _navigationScrollVersion;
    private readonly List<SettingRowLayoutState> _settingRows = [];
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
        _suppressCategoryScroll = true;
        _suppressScrollSync = true;
        SettingsCategoryList.SelectedIndex = 0;
        _suppressCategoryScroll = false;
        SelectCategoryBySection("DefaultsSection");
        SettingsScroll.ChangeView(null, 0, null, disableAnimation: true);
        DispatcherQueue.TryEnqueue(() =>
        {
            SettingsScroll.UpdateLayout();
            SettingsScroll.ChangeView(null, 0, null, disableAnimation: true);
            _suppressScrollSync = false;
            NavigateToSectionByName(string.IsNullOrWhiteSpace(initialSectionName)
                ? "DefaultsSection"
                : initialSectionName);
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

    private void OpenThresholdSettings_Click(object sender, RoutedEventArgs e) =>
        NavigateToThresholdSettings();

    private void OpenDetectionRules_Click(object sender, RoutedEventArgs e) =>
        NavigateToDetectionRules();
}
