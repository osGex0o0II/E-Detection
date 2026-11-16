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

    private void InitializeDetectionConfig(string? preferredPath)
    {
        var preferredConfigPath = DetectionConfigService.ResolveEffectiveConfigPath(preferredPath);
        var originalPath = preferredConfigPath;
        var isCustomPath = !string.Equals(
            Path.GetFullPath(DetectionConfigService.ResolveConfigPath(preferredConfigPath)),
            Path.GetFullPath(DetectionConfigService.DefaultUserConfigPath),
            StringComparison.OrdinalIgnoreCase);
        var configLoaded = _detectionConfig.TryLoadValidated(preferredConfigPath, out var loadedConfig, out var configError);
        var requiresConfirmation = isCustomPath && !configLoaded;

        if (!configLoaded)
        {
            preferredConfigPath = _detectionConfig.EnsureUserConfig();
            configLoaded = _detectionConfig.TryLoadValidated(preferredConfigPath, out loadedConfig, out var fallbackError);
            if (!configLoaded)
            {
                loadedConfig = _detectionConfig.CreateDefault();
                configError = $"无法安全加载阈值文件：{originalPath}；默认配置也不可用：{fallbackError}";
            }
            else
            {
                configError = requiresConfirmation
                    ? $"原阈值文件不可用：{originalPath}。已展示默认阈值，但必须重新加载有效文件后才能开始检测。"
                    : null;
            }
        }

        ConfigPath = preferredConfigPath;
        _activeDetectionConfigPath = configLoaded && !requiresConfirmation
            ? Path.GetFullPath(DetectionConfigService.ResolveConfigPath(ConfigPath))
            : "";
        ApplyDetectionConfig(loadedConfig);
        AcceptDetectionConfigAsSaved();
        if (requiresConfirmation)
        {
            PendingConfigPath = originalPath;
        }

        if (configError is not null)
        {
            ShowSettingsFeedback(configError, InfoBarSeverity.Warning);
        }

        NotifyDetectionConfigSessionChanged();
    }

    private void ApplyDetectionConfig(DetectionConfigSettings config)
    {
        _applyingDetectionConfig = true;
        try
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
        finally
        {
            _applyingDetectionConfig = false;
        }
    }

    private void RefreshDetectionConfigDirty()
    {
        if (_applyingDetectionConfig)
        {
            return;
        }

        IsDetectionConfigDirty = JsonSerializer.Serialize(CaptureDetectionConfig())
            != JsonSerializer.Serialize(_loadedDetectionConfig);
    }

    private void NotifyDetectionConfigSessionChanged()
    {
        OnPropertyChanged(nameof(DetectionConfigStatusText));
        OnPropertyChanged(nameof(RunSetupReadinessText));
        OnPropertyChanged(nameof(RunSetupReadinessDetailText));
        StartCommand.NotifyCanExecuteChanged();
    }

    private void AcceptDetectionConfigAsSaved()
    {
        _loadedDetectionConfig = CaptureDetectionConfig();
        IsDetectionConfigDirty = false;
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
            if (string.IsNullOrWhiteSpace(_activeDetectionConfigPath)
                || !string.Equals(
                    Path.GetFullPath(DetectionConfigService.ResolveConfigPath(ConfigPath)),
                    _activeDetectionConfigPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("当前配置文件尚未安全加载，请先加载有效文件或选择另存为。");
            }

            _detectionConfig.Save(_activeDetectionConfigPath, CaptureDetectionConfig());
            AcceptDetectionConfigAsSaved();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            var message = $"检测阈值保存失败: {ex.Message}";
            ShowSettingsFeedback(message, InfoBarSeverity.Warning);
            AddLog("设置警告", message);
            return false;
        }
    }

    private string EnsureEffectiveConfigPath() => _activeDetectionConfigPath;

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
