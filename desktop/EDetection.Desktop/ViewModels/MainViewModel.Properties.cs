using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDetection.Desktop.Models;
using EDetection.Desktop.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace EDetection.Desktop.ViewModels;

public partial class MainViewModel
    : ObservableObject,
        INetworkProxySettings,
        INtfyNotificationSettings,
        ILlmAssistantSettings
{

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShellStatus))]
    public partial TaskbarProgressKind TaskbarProgressKind { get; set; } = TaskbarProgressKind.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShellStatus))]
    public partial double TaskbarProgressPercent { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyPropertyChangedFor(nameof(RunSetupReadinessText))]
    [NotifyPropertyChangedFor(nameof(RunSetupReadinessDetailText))]
    public partial string InputDirectory { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyPropertyChangedFor(nameof(RunSetupReadinessDetailText))]
    public partial string OutputDirectory { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyPropertyChangedFor(nameof(RunSetupReadinessText))]
    [NotifyPropertyChangedFor(nameof(RunSetupReadinessDetailText))]
    [NotifyPropertyChangedFor(nameof(DetectionConfigStatusText))]
    public partial string ConfigPath { get; set; } = "config.json";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingConfigPathChange))]
    [NotifyPropertyChangedFor(nameof(DetectionConfigStatusText))]
    public partial string PendingConfigPath { get; set; } = "config.json";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetectionConfigStatusText))]
    [NotifyPropertyChangedFor(nameof(RunSetupReadinessText))]
    [NotifyPropertyChangedFor(nameof(RunSetupReadinessDetailText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool IsDetectionConfigDirty { get; set; }

    public bool HasPendingConfigPathChange =>
        !string.Equals(ConfigPath, PendingConfigPath, StringComparison.OrdinalIgnoreCase);

    public string DetectionConfigStatusText =>
        IsDetectionConfigDirty
            ? $"阈值有未保存的修改。当前文件：{ConfigPath}"
            : HasPendingConfigPathChange
                ? $"待加载文件尚未生效。当前文件：{ConfigPath}"
                : $"当前生效文件：{ConfigPath}";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool WriteReport { get; set; } = true;

    [ObservableProperty]
    public partial bool CloseToTrayOnClose { get; set; }

    [ObservableProperty]
    public partial bool StartMinimizedToTray { get; set; }

    [ObservableProperty]
    public partial bool AutoStartOnSignIn { get; set; }

    [ObservableProperty]
    public partial string StartupIntegrationStatusText { get; set; } = "登录后自动启动未启用";

    [ObservableProperty]
    public partial bool EnableDesktopNotifications { get; set; } = true;

    [ObservableProperty]
    public partial bool EnableLlmAssistant { get; set; }

    [ObservableProperty]
    public partial string LlmEndpoint { get; set; } = "";

    [ObservableProperty]
    public partial string LlmModel { get; set; } = "";

    [ObservableProperty]
    public partial bool UseProxyForLlm { get; set; }

    [ObservableProperty]
    public partial string PendingLlmApiKey { get; set; } = "";

    [ObservableProperty]
    public partial string LlmApiKeyStatusText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestLlmConnectionCommand))]
    public partial bool IsTestingLlmConnection { get; set; }

    [ObservableProperty]
    public partial bool EnableNtfyNotifications { get; set; }

    [ObservableProperty]
    public partial string NtfyServerUrl { get; set; } = "https://ntfy.sh";

    [ObservableProperty]
    public partial string NtfyTopic { get; set; } = "";

    [ObservableProperty]
    public partial int SelectedNtfyPriorityIndex { get; set; } = 2;

    [ObservableProperty]
    public partial bool UseProxyForNotifications { get; set; }

    [ObservableProperty]
    public partial string PendingNtfyToken { get; set; } = "";

    [ObservableProperty]
    public partial string NtfyTokenStatusText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendTestNtfyNotificationCommand))]
    public partial bool IsSendingTestNtfyNotification { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditProxyCredentials))]
    public partial bool EnableNetworkProxy { get; set; }

    [ObservableProperty]
    public partial string ProxyAddress { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditProxyCredentials))]
    public partial bool ProxyRequiresAuthentication { get; set; }

    public bool CanEditProxyCredentials => EnableNetworkProxy && ProxyRequiresAuthentication;

    [ObservableProperty]
    public partial string ProxyUserName { get; set; } = "";

    [ObservableProperty]
    public partial string PendingProxyPassword { get; set; } = "";

    [ObservableProperty]
    public partial string ProxyPasswordStatusText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestNetworkProxyCommand))]
    public partial bool IsTestingNetworkProxy { get; set; }

    [ObservableProperty]
    public partial bool EnableUpdateChecks { get; set; } = true;

    [ObservableProperty]
    public partial bool UseProxyForUpdates { get; set; }

    [ObservableProperty]
    public partial int SelectedUpdateChannelIndex { get; set; }

    [ObservableProperty]
    public partial string UpdateFeedUrl { get; set; } = "https://github.com/osGex0o0II/E-Detection/releases/latest";

    [ObservableProperty]
    public partial string UpdateStatusText { get; set; } = "尚未检查更新";

    [ObservableProperty]
    public partial string LatestReleaseUrl { get; set; } = "";

    [ObservableProperty]
    public partial string LatestInstallerName { get; set; } = "";

    [ObservableProperty]
    public partial string LatestInstallerDownloadUrl { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenUpdateFeedCommand))]
    public partial bool IsCheckingForUpdates { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenUpdateFeedCommand))]
    public partial bool IsDownloadingUpdateInstaller { get; set; }

    public bool ShouldCheckForUpdatesOnStartup =>
        EnableUpdateChecks && SelectedUpdateChannelIndex == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShellStatus))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanEditRunConfiguration))]
    [NotifyPropertyChangedFor(nameof(CancelCommandVisibility))]
    [NotifyPropertyChangedFor(nameof(StatusProgressVisibility))]
    [NotifyPropertyChangedFor(nameof(RunTelemetryVisibility))]
    [NotifyPropertyChangedFor(nameof(CompletionActionsVisibility))]
    [NotifyPropertyChangedFor(nameof(FirstRunGuideVisibility))]
    [NotifyPropertyChangedFor(nameof(FailureActionsVisibility))]
    [NotifyPropertyChangedFor(nameof(WorkbenchActivityVisibility))]
    [NotifyPropertyChangedFor(nameof(WorkbenchContextText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchStatusBadgeText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchStatusBadgeVisibility))]
    [NotifyPropertyChangedFor(nameof(RunSetupReadinessText))]
    [NotifyPropertyChangedFor(nameof(RunSetupReadinessDetailText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsFromPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetSettingsToDefaultsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseSelectedReportDirectoriesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyCompletionSummaryCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShellStatus))]
    [NotifyPropertyChangedFor(nameof(WorkbenchStatusText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchStatusBadgeText))]
    [NotifyPropertyChangedFor(nameof(FirstRunGuideTitleText))]
    [NotifyPropertyChangedFor(nameof(FirstRunGuideSubtitleText))]
    [NotifyPropertyChangedFor(nameof(FailureActionsVisibility))]
    [NotifyPropertyChangedFor(nameof(WorkbenchActivityVisibility))]
    [NotifyPropertyChangedFor(nameof(FailureActionTitleText))]
    [NotifyPropertyChangedFor(nameof(FailureActionBodyText))]
    [NotifyPropertyChangedFor(nameof(CompletionActionsVisibility))]
    [NotifyPropertyChangedFor(nameof(WorkbenchActivityTitleText))]
    [NotifyPropertyChangedFor(nameof(CompletionActionTitleText))]
    [NotifyPropertyChangedFor(nameof(CompletionActionBodyText))]
    [NotifyPropertyChangedFor(nameof(RunSetupReadinessText))]
    [NotifyPropertyChangedFor(nameof(RunSetupReadinessDetailText))]
    [NotifyCanExecuteChangedFor(nameof(CopyCompletionSummaryCommand))]
    public partial string StatusText { get; set; } = "就绪";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProcessedSummary))]
    [NotifyPropertyChangedFor(nameof(WorkbenchProcessedSummary))]
    [NotifyPropertyChangedFor(nameof(CompletionActionBodyText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchProgressText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchProgressPercent))]
    [NotifyPropertyChangedFor(nameof(RunTelemetryVisibility))]
    [NotifyPropertyChangedFor(nameof(FirstRunGuideVisibility))]
    public partial int TotalFiles { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProcessedSummary))]
    [NotifyPropertyChangedFor(nameof(WorkbenchProcessedSummary))]
    [NotifyPropertyChangedFor(nameof(CompletionActionBodyText))]
    [NotifyPropertyChangedFor(nameof(RunTelemetryVisibility))]
    public partial int ProcessedFiles { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkbenchAnomalyFiles))]
    [NotifyPropertyChangedFor(nameof(CompletionActionTitleText))]
    [NotifyPropertyChangedFor(nameof(CompletionActionBodyText))]
    public partial int AnomalyFiles { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkbenchAnomalyRecords))]
    [NotifyPropertyChangedFor(nameof(CompletionActionTitleText))]
    [NotifyPropertyChangedFor(nameof(CompletionActionBodyText))]
    public partial int AnomalyRecords { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkbenchSkippedFiles))]
    [NotifyPropertyChangedFor(nameof(CompletionActionBodyText))]
    public partial int SkippedFiles { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchProgressText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchProgressPercent))]
    public partial double ProgressPercent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkbenchReportPath))]
    [NotifyPropertyChangedFor(nameof(WorkbenchReportPathText))]
    [NotifyPropertyChangedFor(nameof(ReportActionsVisibility))]
    [NotifyPropertyChangedFor(nameof(WorkbenchActivityVisibility))]
    [NotifyPropertyChangedFor(nameof(WorkbenchActivityTitleText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchContextText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchStatusBadgeText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchStatusBadgeVisibility))]
    [NotifyPropertyChangedFor(nameof(CompletionActionBodyText))]
    [NotifyPropertyChangedFor(nameof(FirstRunGuideVisibility))]
    [NotifyPropertyChangedFor(nameof(RunSetupReadinessText))]
    [NotifyPropertyChangedFor(nameof(RunSetupReadinessDetailText))]
    [NotifyCanExecuteChangedFor(nameof(OpenCurrentReportCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenCurrentReportFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyCurrentReportPathCommand))]
    public partial string ReportPath { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDetail))]
    [NotifyCanExecuteChangedFor(nameof(CopySelectedDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSelectedDetailSourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopySelectedDetailSourcePathCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExplainSelectedDetailCommand))]
    public partial ReportDetailPreview? SelectedDetail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingReportSnapshot))]
    [NotifyPropertyChangedFor(nameof(WorkbenchModeText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchStatusText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchProgressText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchProgressPercent))]
    [NotifyPropertyChangedFor(nameof(WorkbenchProcessedSummary))]
    [NotifyPropertyChangedFor(nameof(WorkbenchAnomalyFiles))]
    [NotifyPropertyChangedFor(nameof(WorkbenchAnomalyRecords))]
    [NotifyPropertyChangedFor(nameof(WorkbenchSkippedFiles))]
    [NotifyPropertyChangedFor(nameof(WorkbenchReportPath))]
    [NotifyPropertyChangedFor(nameof(WorkbenchReportPathText))]
    [NotifyPropertyChangedFor(nameof(ReportActionsVisibility))]
    [NotifyPropertyChangedFor(nameof(WorkbenchActivityVisibility))]
    [NotifyPropertyChangedFor(nameof(WorkbenchActivityTitleText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchReportButtonText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchReportFolderButtonText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchContextText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchStatusBadgeText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchStatusBadgeVisibility))]
    [NotifyPropertyChangedFor(nameof(WorkbenchReportDeviceCount))]
    [NotifyPropertyChangedFor(nameof(WorkbenchHighRiskDevices))]
    [NotifyPropertyChangedFor(nameof(WorkbenchTopIssueTypes))]
    [NotifyPropertyChangedFor(nameof(WorkbenchSensorOverviewText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchDetailPreviewTotalCount))]
    [NotifyPropertyChangedFor(nameof(WorkbenchFilteredDetailPreview))]
    [NotifyPropertyChangedFor(nameof(WorkbenchDetailPreviewStatusText))]
    [NotifyPropertyChangedFor(nameof(FirstRunGuideVisibility))]
    [NotifyPropertyChangedFor(nameof(SnapshotActionsVisibility))]
    [NotifyPropertyChangedFor(nameof(RunTelemetryVisibility))]
    [NotifyPropertyChangedFor(nameof(CompletionActionsVisibility))]
    [NotifyCanExecuteChangedFor(nameof(OpenCurrentReportCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenCurrentReportFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyCurrentReportPathCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowCurrentRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyCompletionSummaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFirstAnomalySourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSelectedReportCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSelectedReportFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseSelectedReportDirectoriesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedReportCommand))]
    public partial RecentReport? SelectedReport { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkbenchReportDeviceCount))]
    public partial int ReportDeviceCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SensorOverviewText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchSensorOverviewText))]
    public partial int SensorOfflineDevices { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SensorOverviewText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchSensorOverviewText))]
    public partial int SensorFaultRows { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SensorOverviewText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchSensorOverviewText))]
    public partial int SensorMissingRows { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SensorOverviewText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchSensorOverviewText))]
    public partial int SensorSkippedRows { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkbenchFilteredDetailPreview))]
    [NotifyPropertyChangedFor(nameof(WorkbenchDetailPreviewStatusText))]
    public partial string DetailSearchText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkbenchFilteredDetailPreview))]
    [NotifyPropertyChangedFor(nameof(WorkbenchDetailPreviewStatusText))]
    public partial int SelectedSeverityFilterIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkbenchFilteredDetailPreview))]
    [NotifyPropertyChangedFor(nameof(WorkbenchDetailPreviewStatusText))]
    public partial int SelectedIssueTypeFilterIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailSortStatusText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchFilteredDetailPreview))]
    [NotifyPropertyChangedFor(nameof(WorkbenchDetailPreviewStatusText))]
    public partial string SelectedDetailSortKey { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailPreviewStatusText))]
    [NotifyPropertyChangedFor(nameof(WorkbenchDetailPreviewTotalCount))]
    [NotifyPropertyChangedFor(nameof(WorkbenchDetailPreviewStatusText))]
    public partial int DetailPreviewTotalCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LlmDetailExplanationVisibility))]
    public partial string LlmDetailExplanationText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExplainSelectedDetailCommand))]
    public partial bool IsExplainingSelectedDetail { get; set; }

    public Visibility LlmDetailExplanationVisibility =>
        string.IsNullOrWhiteSpace(LlmDetailExplanationText) ? Visibility.Collapsed : Visibility.Visible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeMode))]
    public partial int SelectedThemeIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackdropMode))]
    public partial int SelectedBackdropIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PoetryStatusVisibility))]
    [NotifyCanExecuteChangedFor(nameof(RefreshPoetryStatusCommand))]
    public partial bool EnablePoetryStatus { get; set; }

    [ObservableProperty]
    public partial string PoetryServiceUrl { get; set; } = "https://poetry.palemoky.com/";

    [ObservableProperty]
    public partial int SelectedPoetryLanguageIndex { get; set; }

    [ObservableProperty]
    public partial string PoetryStatusText { get; set; } = "山重水复疑无路，柳暗花明又一村。";

    [ObservableProperty]
    public partial string PoetryStatusSourceText { get; set; } = "陆游 · 游山西村";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshPoetryStatusCommand))]
    public partial bool IsRefreshingPoetryStatus { get; set; }

    public Visibility PoetryStatusVisibility =>
        EnablePoetryStatus ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    public partial bool IsSettingsFeedbackOpen { get; set; }

    [ObservableProperty]
    public partial string SettingsFeedbackText { get; set; } = "";

    [ObservableProperty]
    public partial InfoBarSeverity SettingsFeedbackSeverity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial double VoltageMinThreshold { get; set; } = 353.0;

    [ObservableProperty]
    public partial double VoltageMaxThreshold { get; set; } = 430.0;

    [ObservableProperty]
    public partial double CurrentMaxThreshold { get; set; } = 1000.0;

    [ObservableProperty]
    public partial double CurrentUnbalanceMaxThreshold { get; set; } = 0.15;

    [ObservableProperty]
    public partial double ActivePowerMinThreshold { get; set; }

    [ObservableProperty]
    public partial double PowerFactorMinThreshold { get; set; } = 0.9;

    [ObservableProperty]
    public partial double TemperatureMinThreshold { get; set; }

    [ObservableProperty]
    public partial double TemperatureMaxThreshold { get; set; } = 70.0;

    [ObservableProperty]
    public partial double CurrentActiveMinThreshold { get; set; } = 1.0;

    [ObservableProperty]
    public partial double FreezeCountThreshold { get; set; } = 3;

    [ObservableProperty]
    public partial double FreezeStdThreshold { get; set; } = 0.01;

    [ObservableProperty]
    public partial double VoltageImbalanceThreshold { get; set; } = 0.02;

    [ObservableProperty]
    public partial bool CurrentOverloadEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool CurrentUnbalanceEnabled { get; set; }

    [ObservableProperty]
    public partial bool PowerFactorEnabled { get; set; }

    [ObservableProperty]
    public partial bool DetailOutputEnabled { get; set; }

    public string DiagnosticSummaryText => Diagnostics.SummaryText;

    public string BackendDiagnosticText => Diagnostics.BackendText;

    public string ConfigDiagnosticText => Diagnostics.ConfigText;

    public string InputDiagnosticText => Diagnostics.InputText;

    public string OutputDiagnosticText => Diagnostics.OutputText;

    public string DiagnosticCheckedAtText => Diagnostics.CheckedAtText;

    public string DiagnosticActionText => Diagnostics.ActionText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveRunSummaryVisibility))]
    [NotifyPropertyChangedFor(nameof(WorkbenchContextText))]
    public partial string ActiveRunSummaryText { get; set; } = "";

    public string CurrentFileText => RunTelemetry.CurrentFileText;

    public string RunElapsedText => RunTelemetry.ElapsedText;

    public string RunSpeedText => RunTelemetry.SpeedText;

    public string RunRemainingText => RunTelemetry.RemainingText;

    public string RunProgressDetailText => RunTelemetry.ProgressDetailText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CancelConfirmationVisibility))]
    [NotifyPropertyChangedFor(nameof(CancelButtonText))]
    public partial bool IsCancelConfirmationPending { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FailureActionBodyText))]
    [NotifyPropertyChangedFor(nameof(FailureActionsVisibility))]
    public partial string LastFailureText { get; set; } = "暂无失败";

    public int SelectedLogRetentionIndex => RuntimeLogs.SelectedRetentionIndex;

    public string LogSearchText => RuntimeLogs.SearchText;

    public int SelectedLogKindFilterIndex => RuntimeLogs.SelectedKindFilterIndex;

    partial void OnSelectedThemeIndexChanged(int value) => SaveAppearanceSettings();

    partial void OnSelectedBackdropIndexChanged(int value) => SaveAppearanceSettings();

    partial void OnEnablePoetryStatusChanged(bool value)
    {
        SaveAppearanceSettings();
        if (value)
        {
            _ = RefreshPoetryStatusAsync();
        }
    }

    partial void OnPoetryServiceUrlChanged(string value) => SaveAppearanceSettings();

    partial void OnSelectedPoetryLanguageIndexChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, 1);
        if (normalized != value)
        {
            SelectedPoetryLanguageIndex = normalized;
            return;
        }

        SaveAppearanceSettings();
        if (EnablePoetryStatus)
        {
            _ = RefreshPoetryStatusAsync();
        }
    }

    partial void OnCloseToTrayOnCloseChanged(bool value) => SavePreferenceSettings();

    partial void OnStartMinimizedToTrayChanged(bool value) => SavePreferenceSettings();

    partial void OnAutoStartOnSignInChanged(bool value)
    {
        if (!_settingsLoaded || _syncingStartupPreference)
        {
            UpdateStartupIntegrationStatus(_startup.GetStatus());
            return;
        }

        try
        {
            _startup.SetEnabled(value);
            UpdateStartupIntegrationStatus(_startup.GetStatus());
            RefreshDesktopHealth();
            SavePreferenceSettings();
        }
        catch (Exception ex)
        {
            StartupIntegrationStatusText = value
                ? $"启用登录自启动失败: {ex.Message}"
                : $"关闭登录自启动失败: {ex.Message}";
            _syncingStartupPreference = true;
            var startupStatus = _startup.GetStatus();
            AutoStartOnSignIn = startupStatus.IsEnabled;
            _syncingStartupPreference = false;
            RefreshDesktopHealth();
            SavePreferenceSettings();
        }
    }

    partial void OnEnableDesktopNotificationsChanged(bool value)
    {
        RefreshDesktopHealth();
        SavePreferenceSettings();
    }

    partial void OnEnableLlmAssistantChanged(bool value) => SavePreferenceSettings();

    partial void OnLlmEndpointChanged(string value) => SavePreferenceSettings();

    partial void OnLlmModelChanged(string value) => SavePreferenceSettings();

    partial void OnUseProxyForLlmChanged(bool value) => SavePreferenceSettings();

    partial void OnEnableNtfyNotificationsChanged(bool value) => SavePreferenceSettings();

    partial void OnNtfyServerUrlChanged(string value) => SavePreferenceSettings();

    partial void OnNtfyTopicChanged(string value) => SavePreferenceSettings();

    partial void OnSelectedNtfyPriorityIndexChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, 4);
        if (normalized != value)
        {
            SelectedNtfyPriorityIndex = normalized;
            return;
        }

        SavePreferenceSettings();
    }

    partial void OnUseProxyForNotificationsChanged(bool value) => SavePreferenceSettings();

    partial void OnEnableNetworkProxyChanged(bool value) => SavePreferenceSettings();

    partial void OnProxyAddressChanged(string value) => SavePreferenceSettings();

    partial void OnProxyRequiresAuthenticationChanged(bool value) => SavePreferenceSettings();

    partial void OnProxyUserNameChanged(string value) => SavePreferenceSettings();

    partial void OnEnableUpdateChecksChanged(bool value) => SavePreferenceSettings();

    partial void OnUseProxyForUpdatesChanged(bool value) => SavePreferenceSettings();

    partial void OnSelectedUpdateChannelIndexChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, 1);
        if (normalized != value)
        {
            SelectedUpdateChannelIndex = normalized;
            return;
        }

        SavePreferenceSettings();
    }

    partial void OnUpdateFeedUrlChanged(string value) => SavePreferenceSettings();

    partial void OnWriteReportChanged(bool value)
    {
        RefreshLocalDiagnostics();
        SavePreferenceSettings();
    }

    partial void OnInputDirectoryChanged(string value)
    {
        RefreshLocalDiagnostics();
        RefreshFirstRunGuide();
        SavePreferenceSettings();
    }

    partial void OnOutputDirectoryChanged(string value)
    {
        RefreshLocalDiagnostics();
        RefreshFirstRunGuide();
        SavePreferenceSettings();
    }

    partial void OnConfigPathChanged(string value)
    {
        PendingConfigPath = value;
        OnPropertyChanged(nameof(HasPendingConfigPathChange));
        OnPropertyChanged(nameof(DetectionConfigStatusText));
        RefreshLocalDiagnostics();
        RefreshFirstRunGuide();
        SavePreferenceSettings();
    }

    partial void OnVoltageMinThresholdChanged(double value) => RefreshDetectionConfigDirty();
    partial void OnVoltageMaxThresholdChanged(double value) => RefreshDetectionConfigDirty();
    partial void OnCurrentMaxThresholdChanged(double value) => RefreshDetectionConfigDirty();
    partial void OnCurrentUnbalanceMaxThresholdChanged(double value) => RefreshDetectionConfigDirty();
    partial void OnActivePowerMinThresholdChanged(double value) => RefreshDetectionConfigDirty();
    partial void OnPowerFactorMinThresholdChanged(double value) => RefreshDetectionConfigDirty();
    partial void OnTemperatureMinThresholdChanged(double value) => RefreshDetectionConfigDirty();
    partial void OnTemperatureMaxThresholdChanged(double value) => RefreshDetectionConfigDirty();
    partial void OnCurrentActiveMinThresholdChanged(double value) => RefreshDetectionConfigDirty();
    partial void OnFreezeCountThresholdChanged(double value) => RefreshDetectionConfigDirty();
    partial void OnFreezeStdThresholdChanged(double value) => RefreshDetectionConfigDirty();
    partial void OnVoltageImbalanceThresholdChanged(double value) => RefreshDetectionConfigDirty();
    partial void OnCurrentOverloadEnabledChanged(bool value) => RefreshDetectionConfigDirty();
    partial void OnCurrentUnbalanceEnabledChanged(bool value) => RefreshDetectionConfigDirty();
    partial void OnPowerFactorEnabledChanged(bool value) => RefreshDetectionConfigDirty();
    partial void OnDetailOutputEnabledChanged(bool value) => RefreshDetectionConfigDirty();

    partial void OnSelectedReportChanged(RecentReport? value)
    {
        if (!ReferenceEquals(ReportHistory.SelectedReport, value))
        {
            ReportHistory.SelectedReport = value;
        }

        SelectedDetail = null;
        RefreshDetailPreview();
    }

    partial void OnDetailSearchTextChanged(string value) => RefreshDetailPreview();

    partial void OnSelectedSeverityFilterIndexChanged(int value) => RefreshDetailPreview();

    partial void OnSelectedIssueTypeFilterIndexChanged(int value) => RefreshDetailPreview();

    partial void OnSelectedDetailSortKeyChanged(string value) => RefreshDetailPreview();

    partial void OnSelectedDetailChanged(ReportDetailPreview? value)
    {
        if (!IsExplainingSelectedDetail)
        {
            LlmDetailExplanationText = "";
        }
    }

}
