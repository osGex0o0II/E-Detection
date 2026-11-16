using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;
using EDetection.Desktop.ViewModels;

namespace EDetection.Desktop.Views;

public sealed partial class AppShellView : UserControl
{
    private MainViewModel? _observedViewModel;
    private bool? _lastCompactLayout;
    private bool _isSettingsPageVisible;
    private bool _applyingTitleBarInset;
    private const double StackedStatusWidth = 1008;
    private const double CompactShellWidth = 1080;
    private const double ComfortableShellWidth = 1100;
    private const double CompactSetupRowWeight = 0.42;
    private const double CompactWorkbenchRowWeight = 0.58;
    private const double CompactSetupRowMinHeight = 190;
    private const double CompactWorkbenchRowMinHeight = 280;

    public AppShellView()
    {
        InitializeComponent();
        Loaded += AppShellView_Loaded;
        DataContextChanged += AppShellView_DataContextChanged;
        AppTitleBar.SizeChanged += (_, _) => QueueTitleBarInsetCorrection();
        AppTitleBar.LayoutUpdated += (_, _) => ApplyTitleBarInsetCorrection();
        ShellSettings.BackRequested += (_, _) => ShowWorkbench();
        ShellSettings.AboutRequested += (_, _) => AboutRequested?.Invoke(this, EventArgs.Empty);
    }

    public FrameworkElement TitleBarElement => AppTitleBar;

    public FrameworkElement RootElement => Root;

    public event EventHandler? AboutRequested;

    private void SettingsButton_Click(object sender, RoutedEventArgs e) =>
        ShowSettingsPage();

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width);

    private void AppShellView_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _observedViewModel = null;
        }

        if (args.NewValue is MainViewModel newViewModel)
        {
            newViewModel.PropertyChanged += ViewModel_PropertyChanged;
            _observedViewModel = newViewModel;
        }

        ApplyResponsiveLayout(Root.ActualWidth);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsRunning))
        {
            ApplyResponsiveLayout(Root.ActualWidth);
        }
    }

    public void ShowSettingsPage(string? initialSectionName = null)
    {
        if (_isSettingsPageVisible)
        {
            if (!string.IsNullOrWhiteSpace(initialSectionName))
            {
                ShellSettings.NavigateToSectionByName(initialSectionName);
            }

            return;
        }

        _isSettingsPageVisible = true;
        StatusBand.Visibility = Visibility.Collapsed;
        WorkbenchLayout.Visibility = Visibility.Collapsed;
        ShellSettings.Visibility = Visibility.Visible;
        SettingsTitleBarButton.Visibility = Visibility.Collapsed;
        ShellSettings.PrepareForDisplay(initialSectionName);
        ShellSettings.PlayEntranceAnimation(forward: true);
        ShellSettings.FocusSearchOrNavigation();
    }

    public void OpenSettingsSection(string sectionName) =>
        ShowSettingsPage(sectionName);

    public void ShowWorkbench()
    {
        if (!_isSettingsPageVisible)
        {
            return;
        }

        _isSettingsPageVisible = false;
        ShellSettings.Visibility = Visibility.Collapsed;
        StatusBand.Visibility = Visibility.Visible;
        WorkbenchLayout.Visibility = Visibility.Visible;
        SettingsTitleBarButton.Visibility = Visibility.Visible;
        ApplyResponsiveLayout(Root.ActualWidth);
        PlayWorkbenchEntranceAnimation();
        SettingsTitleBarButton.Focus(FocusState.Programmatic);
    }

    private void PlayWorkbenchEntranceAnimation()
    {
        if (!new UISettings().AnimationsEnabled)
        {
            StatusBand.Opacity = 1;
            WorkbenchLayout.Opacity = 1;
            return;
        }

        StatusBand.Opacity = 0;
        WorkbenchLayout.Opacity = 0;

        var storyboard = new Storyboard();
        foreach (var target in new FrameworkElement[] { StatusBand, WorkbenchLayout })
        {
            var animation = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(160),
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);
        }

        storyboard.Begin();
    }

    private void AppShellView_Loaded(object sender, RoutedEventArgs e)
    {
        QueueTitleBarInsetCorrection();
        if (Root.ActualWidth < CompactShellWidth)
        {
            QueueCompactScrollReset();
        }
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (_isSettingsPageVisible)
        {
            return;
        }

        QueueTitleBarInsetCorrection();
        var compact = width < CompactShellWidth;
        var reduced = width < ComfortableShellWidth;
        var showProgress = ViewModel?.IsRunning == true;
        var stackedStatus = compact && width < StackedStatusWidth;
        var layoutModeChanged = _lastCompactLayout != compact;
        _lastCompactLayout = compact;

        StatusLayout.RowDefinitions[1].Height = stackedStatus ? GridLength.Auto : new GridLength(0);
        StatusLayout.RowDefinitions[2].Height = compact ? GridLength.Auto : new GridLength(0);
        StatusLayout.ColumnSpacing = stackedStatus ? 0 : compact ? 12 : 18;
        StatusLayout.RowSpacing = stackedStatus ? 6 : 8;
        StatusBand.Padding = compact
            ? new Thickness(16, 8, 16, 8)
            : new Thickness(24, 8, 24, 8);
        WorkbenchLayout.Padding = compact
            ? new Thickness(16)
            : new Thickness(24);
        WorkbenchLayout.ColumnSpacing = reduced ? 16 : 20;
        WorkbenchLayout.RowSpacing = compact ? 16 : 0;

        if (compact)
        {
            StatusTextColumn.Width = new GridLength(1, GridUnitType.Star);
            StatusProgressColumn.Width = showProgress
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
            StatusPoetryColumn.Width = !showProgress
                ? new GridLength(1.4, GridUnitType.Star)
                : stackedStatus
                ? new GridLength(0)
                : new GridLength(1, GridUnitType.Star);

            Grid.SetRow(StatusTextPanel, 0);
            Grid.SetColumn(StatusTextPanel, 0);
            Grid.SetColumnSpan(StatusTextPanel, stackedStatus ? 3 : 1);
            Grid.SetRow(ProgressPanel, stackedStatus ? 1 : 0);
            Grid.SetColumn(ProgressPanel, stackedStatus ? 0 : 1);
            Grid.SetColumnSpan(ProgressPanel, stackedStatus ? 3 : 1);
            Grid.SetRow(PoetryPanel, 0);
            Grid.SetColumn(PoetryPanel, showProgress ? 2 : 1);
            Grid.SetColumnSpan(PoetryPanel, showProgress ? 1 : 2);

            SetupColumn.Width = new GridLength(1, GridUnitType.Star);
            WorkbenchColumn.Width = new GridLength(1, GridUnitType.Star);
            WorkbenchLayout.RowDefinitions[0].Height = new GridLength(CompactSetupRowWeight, GridUnitType.Star);
            WorkbenchLayout.RowDefinitions[0].MinHeight = CompactSetupRowMinHeight;
            WorkbenchLayout.RowDefinitions[1].Height = new GridLength(CompactWorkbenchRowWeight, GridUnitType.Star);
            WorkbenchLayout.RowDefinitions[1].MinHeight = CompactWorkbenchRowMinHeight;

            Grid.SetRow(RunSetup, 0);
            Grid.SetColumn(RunSetup, 0);
            Grid.SetColumnSpan(RunSetup, 2);

            Grid.SetRow(WorkbenchScroll, 1);
            Grid.SetColumn(WorkbenchScroll, 0);
            Grid.SetColumnSpan(WorkbenchScroll, 2);
            RunSetup.SetCompactLayout(true);
            if (layoutModeChanged)
            {
                QueueCompactScrollReset();
            }

            return;
        }

        StatusTextColumn.Width = showProgress
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(320);
        StatusProgressColumn.Width = !showProgress
            ? new GridLength(0)
            : reduced
            ? new GridLength(188)
            : new GridLength(220);
        StatusPoetryColumn.Width = new GridLength(1.4, GridUnitType.Star);

        Grid.SetRow(StatusTextPanel, 0);
        Grid.SetColumn(StatusTextPanel, 0);
        Grid.SetColumnSpan(StatusTextPanel, 1);
        Grid.SetRow(ProgressPanel, 0);
        Grid.SetColumn(ProgressPanel, 1);
        Grid.SetColumnSpan(ProgressPanel, 1);
        Grid.SetRow(PoetryPanel, 0);
        Grid.SetColumn(PoetryPanel, showProgress ? 2 : 1);
        Grid.SetColumnSpan(PoetryPanel, showProgress ? 1 : 2);

        SetupColumn.Width = new GridLength(reduced ? 340 : 380);
        WorkbenchColumn.Width = new GridLength(1, GridUnitType.Star);
        WorkbenchLayout.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
        WorkbenchLayout.RowDefinitions[0].MinHeight = 0;
        WorkbenchLayout.RowDefinitions[1].Height = new GridLength(0);
        WorkbenchLayout.RowDefinitions[1].MinHeight = 0;

        Grid.SetRow(RunSetup, 0);
        Grid.SetColumn(RunSetup, 0);
        Grid.SetColumnSpan(RunSetup, 1);

        Grid.SetRow(WorkbenchScroll, 0);
        Grid.SetColumn(WorkbenchScroll, 1);
        Grid.SetColumnSpan(WorkbenchScroll, 1);
        RunSetup.SetCompactLayout(false);
        if (layoutModeChanged)
        {
            WorkbenchScroll.ChangeView(null, 0, null, disableAnimation: true);
        }
    }

    private void QueueCompactScrollReset()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            WorkbenchScroll.UpdateLayout();
            WorkbenchScroll.ChangeView(null, 0, null, disableAnimation: true);
            RunSetup.ResetScroll();
        });
    }

    private void QueueTitleBarInsetCorrection()
    {
        if (_isSettingsPageVisible)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(ApplyTitleBarInsetCorrection);
    }

    private void ApplyTitleBarInsetCorrection()
    {
        if (_applyingTitleBarInset
            || App.CurrentWindow is null
            || XamlRoot is null)
        {
            return;
        }

        var scale = XamlRoot.RasterizationScale;
        var rightInset = App.CurrentWindow.AppWindow.TitleBar.RightInset;
        if (scale <= 0 || rightInset <= 0)
        {
            return;
        }

        // The TitleBar control currently writes the physical right inset into
        // the XAML padding column without dividing by the rasterization scale.
        // At non-100% DPI that pushes the right header inwards by the inset
        // again; keep the padding in DIPs so the header stays next to the
        // caption buttons.
        var insetDip = rightInset / scale;
        _applyingTitleBarInset = true;
        try
        {
            if (AppTitleBar.TrySetRightPaddingDip(insetDip))
            {
                DispatcherQueue.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    AppTitleBar.RecomputeDragRegions);
            }
        }
        finally
        {
            _applyingTitleBarInset = false;
        }
    }
}
