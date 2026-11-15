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
    private static void CopyTextToClipboard(string text)
    {
        var data = new DataPackage();
        data.SetText(text);
        Clipboard.SetContent(data);
    }

    private void SetTaskbarProgress(TaskbarProgressKind kind, double percent)
    {
        TaskbarProgressKind = kind;
        TaskbarProgressPercent = Math.Clamp(percent, 0, 100);
    }

    private void RequestDesktopNotification(
        DesktopNotificationKind kind,
        string title,
        string message,
        string? reportPath = null)
    {
        if (_isShellShutdownRequested)
        {
            return;
        }

        if (_desktopNotificationSent)
        {
            return;
        }

        if (!EnableDesktopNotifications)
        {
            return;
        }

        _desktopNotificationSent = true;
        DesktopNotificationRequested?.Invoke(
            this,
            new DesktopNotificationRequest(kind, title, message, reportPath));
    }

    private void SaveSettings()
    {
        _settings.Save(new AppSettings
        {
            InputDirectory = InputDirectory,
            OutputDirectory = OutputDirectory,
            ConfigPath = ConfigPath,
            WriteReport = WriteReport,
            CloseToTrayOnClose = CloseToTrayOnClose,
            StartMinimizedToTray = StartMinimizedToTray,
            AutoStartOnSignIn = AutoStartOnSignIn,
            EnableDesktopNotifications = EnableDesktopNotifications,
            EnableLlmAssistant = EnableLlmAssistant,
            LlmEndpoint = LlmEndpoint,
            LlmModel = LlmModel,
            UseProxyForLlm = UseProxyForLlm,
            EnableNtfyNotifications = EnableNtfyNotifications,
            NtfyServerUrl = NtfyServerUrl,
            NtfyTopic = NtfyTopic,
            SelectedNtfyPriorityIndex = SelectedNtfyPriorityIndex,
            UseProxyForNotifications = UseProxyForNotifications,
            EnableNetworkProxy = EnableNetworkProxy,
            ProxyAddress = ProxyAddress,
            ProxyRequiresAuthentication = ProxyRequiresAuthentication,
            ProxyUserName = ProxyUserName,
            EnableUpdateChecks = EnableUpdateChecks,
            UseProxyForUpdates = UseProxyForUpdates,
            SelectedUpdateChannelIndex = SelectedUpdateChannelIndex,
            UpdateFeedUrl = UpdateFeedUrl,
            SelectedLogRetentionIndex = SelectedLogRetentionIndex,
            SelectedRecentReportLimitIndex = ReportHistory.SelectedRecentReportLimitIndex,
            RecentReports = RecentReports.ToList(),
            SelectedThemeIndex = SelectedThemeIndex,
            SelectedBackdropIndex = SelectedBackdropIndex,
            EnablePoetryStatus = EnablePoetryStatus,
            PoetryServiceUrl = PoetryServiceUrl,
            SelectedPoetryLanguageIndex = SelectedPoetryLanguageIndex,
            WindowLeft = WindowLeft,
            WindowTop = WindowTop,
            WindowWidth = WindowWidth,
            WindowHeight = WindowHeight,
            IsWindowMaximized = IsWindowMaximized,
        });
    }

    private void ApplySettingsDefaults(AppSettings defaults)
    {
        InputDirectory = defaults.InputDirectory;
        OutputDirectory = defaults.OutputDirectory;
        ConfigPath = _detectionConfig.EnsureUserConfig(defaults.ConfigPath);
        WriteReport = defaults.WriteReport;
        CloseToTrayOnClose = defaults.CloseToTrayOnClose;
        StartMinimizedToTray = defaults.StartMinimizedToTray;
        EnableDesktopNotifications = defaults.EnableDesktopNotifications;
        EnableLlmAssistant = defaults.EnableLlmAssistant;
        LlmEndpoint = defaults.LlmEndpoint;
        LlmModel = defaults.LlmModel;
        UseProxyForLlm = defaults.UseProxyForLlm;
        EnableNtfyNotifications = defaults.EnableNtfyNotifications;
        NtfyServerUrl = defaults.NtfyServerUrl;
        NtfyTopic = defaults.NtfyTopic;
        SelectedNtfyPriorityIndex = defaults.SelectedNtfyPriorityIndex;
        UseProxyForNotifications = defaults.UseProxyForNotifications;
        EnableNetworkProxy = defaults.EnableNetworkProxy;
        ProxyAddress = defaults.ProxyAddress;
        ProxyRequiresAuthentication = defaults.ProxyRequiresAuthentication;
        ProxyUserName = defaults.ProxyUserName;
        EnableUpdateChecks = defaults.EnableUpdateChecks;
        UseProxyForUpdates = defaults.UseProxyForUpdates;
        SelectedUpdateChannelIndex = defaults.SelectedUpdateChannelIndex;
        UpdateFeedUrl = defaults.UpdateFeedUrl;
        RuntimeLogs.SelectedRetentionIndex = defaults.SelectedLogRetentionIndex;
        ReportHistory.SelectedRecentReportLimitIndex = defaults.SelectedRecentReportLimitIndex;
        SelectedThemeIndex = defaults.SelectedThemeIndex;
        SelectedBackdropIndex = defaults.SelectedBackdropIndex;
        EnablePoetryStatus = defaults.EnablePoetryStatus;
        PoetryServiceUrl = defaults.PoetryServiceUrl;
        SelectedPoetryLanguageIndex = defaults.SelectedPoetryLanguageIndex;

        try
        {
            _startup.SetEnabled(defaults.AutoStartOnSignIn);
        }
        catch (Exception ex)
        {
            StartupIntegrationStatusText = $"重置登录自启动失败: {ex.Message}";
        }

        _syncingStartupPreference = true;
        var startupStatus = _startup.GetStatus();
        AutoStartOnSignIn = startupStatus.IsEnabled;
        _syncingStartupPreference = false;
        UpdateStartupIntegrationStatus(startupStatus);
    }

    private void ApplyDetectionConfig(DetectionConfigSettings config)
    {
        VoltageMinThreshold = config.VoltageMinThreshold;
        VoltageMaxThreshold = config.VoltageMaxThreshold;
        CurrentMaxThreshold = config.CurrentMaxThreshold;
        CurrentUnbalanceMaxThreshold = config.CurrentUnbalanceMaxThreshold;
        ActivePowerMinThreshold = config.ActivePowerMinThreshold;
        PowerFactorMinThreshold = config.PowerFactorMinThreshold;
        TemperatureMinThreshold = config.TemperatureMinThreshold;
        TemperatureMaxThreshold = config.TemperatureMaxThreshold;
        CurrentActiveMinThreshold = config.CurrentActiveMinThreshold;
        FreezeCountThreshold = config.FreezeCountThreshold;
        FreezeStdThreshold = config.FreezeStdThreshold;
        VoltageImbalanceThreshold = config.VoltageImbalanceThreshold;
        CurrentOverloadEnabled = config.CurrentOverloadEnabled;
        CurrentUnbalanceEnabled = config.CurrentUnbalanceEnabled;
        PowerFactorEnabled = config.PowerFactorEnabled;
        DetailOutputEnabled = config.DetailOutputEnabled;
    }

    private DetectionConfigSettings CaptureDetectionConfig() => new()
    {
        VoltageMinThreshold = VoltageMinThreshold,
        VoltageMaxThreshold = VoltageMaxThreshold,
        CurrentMaxThreshold = CurrentMaxThreshold,
        CurrentUnbalanceMaxThreshold = CurrentUnbalanceMaxThreshold,
        ActivePowerMinThreshold = ActivePowerMinThreshold,
        PowerFactorMinThreshold = PowerFactorMinThreshold,
        TemperatureMinThreshold = TemperatureMinThreshold,
        TemperatureMaxThreshold = TemperatureMaxThreshold,
        CurrentActiveMinThreshold = CurrentActiveMinThreshold,
        FreezeCountThreshold = FreezeCountThreshold,
        FreezeStdThreshold = FreezeStdThreshold,
        VoltageImbalanceThreshold = VoltageImbalanceThreshold,
        CurrentOverloadEnabled = CurrentOverloadEnabled,
        CurrentUnbalanceEnabled = CurrentUnbalanceEnabled,
        PowerFactorEnabled = PowerFactorEnabled,
        DetailOutputEnabled = DetailOutputEnabled,
    };

    private bool SaveDetectionConfig()
    {
        try
        {
            var configPath = EnsureEffectiveConfigPath();
            _detectionConfig.Save(configPath, CaptureDetectionConfig());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            var message = $"检测阈值保存失败: {ex.Message}";
            ShowSettingsFeedback(message, InfoBarSeverity.Warning);
            AddLog("设置警告", message);
            return false;
        }
    }

    private string EnsureEffectiveConfigPath()
    {
        var configPath = _detectionConfig.EnsureUserConfig(ConfigPath);
        if (!string.Equals(ConfigPath, configPath, StringComparison.OrdinalIgnoreCase))
        {
            _suppressConfigPathReload = true;
            try
            {
                ConfigPath = configPath;
            }
            finally
            {
                _suppressConfigPathReload = false;
            }
        }

        return configPath;
    }

    private void ShowSettingsFeedback(string message, InfoBarSeverity severity)
    {
        SettingsFeedbackText = message;
        SettingsFeedbackSeverity = severity;
        IsSettingsFeedbackOpen = true;
    }

    public void SaveWindowPlacement(int left, int top, int width, int height, bool isMaximized)
    {
        WindowLeft = left;
        WindowTop = top;
        WindowWidth = width;
        WindowHeight = height;
        IsWindowMaximized = isMaximized;
        SaveSettings();
    }

    private void SaveAppearanceSettings()
    {
        if (!_settingsLoaded)
        {
            return;
        }

        SaveSettings();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SavePreferenceSettings()
    {
        if (!_settingsLoaded)
        {
            return;
        }

        SaveSettings();
    }

}
