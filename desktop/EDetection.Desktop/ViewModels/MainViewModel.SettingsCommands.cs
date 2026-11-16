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
    [RelayCommand]
    private void LoadDetectionConfigFromPath()
    {
        if (IsRunning)
        {
            return;
        }

        var validationError = DetectionConfigService.ValidateConfigFile(ConfigPath);
        if (validationError is not null)
        {
            ShowSettingsFeedback(validationError, InfoBarSeverity.Warning);
            return;
        }

        ApplyDetectionConfig(_detectionConfig.Load(ConfigPath));
        RefreshLocalDiagnostics();
        ShowSettingsFeedback("已加载阈值文件，请检查参数后保存。", InfoBarSeverity.Success);
    }

    [RelayCommand]
    private void SaveSettingsFromPage()
    {
        SaveSettings();
        var thresholdsSaved = SaveDetectionConfig();
        RefreshDesktopHealth();
        RefreshLocalDiagnostics();
        if (thresholdsSaved)
        {
            ShowSettingsFeedback("检测阈值已校验并保存。", InfoBarSeverity.Success);
            AddLog("设置", "设置已保存。");
        }
    }

    [RelayCommand]
    private void ResetSettingsToDefaults()
    {
        ApplySettingsDefaults(_settings.CreateDefault());
        ApplyDetectionConfig(_detectionConfig.CreateDefault());
        var thresholdsSaved = SaveDetectionConfig();
        SaveSettings();
        RefreshDesktopHealth();
        RefreshLocalDiagnostics();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
        if (thresholdsSaved)
        {
            ShowSettingsFeedback("设置已恢复默认值。", InfoBarSeverity.Success);
            AddLog("设置", "设置已恢复默认值。");
        }
    }

    [RelayCommand]
    private void SaveLlmApiKey()
    {
        SaveSecureSecret(PendingLlmApiKey, _credentials.SaveLlmApiKey, () => PendingLlmApiKey = "");
    }

    [RelayCommand]
    private void ClearLlmApiKey()
    {
        ClearSecureSecret(_credentials.ClearLlmApiKey, "LLM API Key 已清除。");
    }

    private bool CanTestLlmConnection() => !IsTestingLlmConnection;

    private bool CanRefreshPoetryStatus() => EnablePoetryStatus && !IsRefreshingPoetryStatus;

    [RelayCommand(CanExecute = nameof(CanRefreshPoetryStatus))]
    public async Task RefreshPoetryStatusAsync()
    {
        if (!EnablePoetryStatus || IsRefreshingPoetryStatus)
        {
            return;
        }

        IsRefreshingPoetryStatus = true;
        try
        {
            using var handler = _networkProxy.BuildHandler(this, useProxy: true);
            var snapshot = await _poetryStatus.GetRandomAsync(
                PoetryServiceUrl,
                SelectedPoetryLanguageIndex,
                handler);
            PoetryStatusText = snapshot.Text;
            PoetryStatusSourceText = string.IsNullOrWhiteSpace(snapshot.Source)
                ? "诗泉"
                : snapshot.Source;
        }
        finally
        {
            IsRefreshingPoetryStatus = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanTestLlmConnection))]
    private async Task TestLlmConnectionAsync()
    {
        IsTestingLlmConnection = true;
        try
        {
            var response = await _llmAssistant.TestConnectionAsync(this);
            var detail = string.IsNullOrWhiteSpace(response)
                ? "服务已响应。"
                : $"服务已响应: {response}";
            ShowSettingsFeedback($"LLM 连接测试成功，{detail}", InfoBarSeverity.Success);
            AddLog("智能助手", "LLM 连接测试成功。");
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   or InvalidOperationException
                                   or TaskCanceledException
                                   or UriFormatException
                                   or JsonException)
        {
            ShowSettingsFeedback($"LLM 连接测试失败: {ex.Message}", InfoBarSeverity.Warning);
            AddLog("智能助手提醒", $"LLM 连接测试失败: {ex.Message}");
        }
        finally
        {
            IsTestingLlmConnection = false;
        }
    }

    [RelayCommand]
    private void SaveNtfyToken()
    {
        SaveSecureSecret(PendingNtfyToken, _credentials.SaveNtfyToken, () => PendingNtfyToken = "");
    }

    [RelayCommand]
    private void ClearNtfyToken()
    {
        ClearSecureSecret(_credentials.ClearNtfyToken, "ntfy Token 已清除。");
    }

    [RelayCommand]
    private void TestDesktopNotification()
    {
        if (!EnableDesktopNotifications)
        {
            ShowSettingsFeedback("请先启用桌面通知。", InfoBarSeverity.Warning);
            return;
        }

        DesktopNotificationRequested?.Invoke(
            this,
            new DesktopNotificationRequest(
                DesktopNotificationKind.Success,
                "E-Detection 桌面通知",
                $"桌面通知可以正常显示 · {DateTimeOffset.Now:HH:mm}",
                ForwardToRemoteNotifications: false));
    }

    public void ReportDesktopNotificationResult(DesktopNotificationRequest request, bool shown)
    {
        if (shown)
        {
            AddLog("桌面通知", $"已发送桌面通知: {request.Title}");
            if (request.Kind is DesktopNotificationKind.Success
                && !request.ForwardToRemoteNotifications
                && string.Equals(request.Title, "E-Detection 桌面通知", StringComparison.Ordinal))
            {
                ShowSettingsFeedback("已发送桌面通知测试。", InfoBarSeverity.Success);
            }

            return;
        }

        AddLog("桌面通知提醒", $"Windows 未显示桌面通知: {request.Title}");
        if (request.Kind is DesktopNotificationKind.Success && !request.ForwardToRemoteNotifications)
        {
            ShowSettingsFeedback("Windows 未显示桌面通知，请检查系统通知设置。", InfoBarSeverity.Warning);
        }
    }

    [RelayCommand]
    private void OpenSystemNotificationSettings()
    {
        OpenUri(WindowsNotificationSettingsUri);
        ShowSettingsFeedback("已打开 Windows 通知设置。", InfoBarSeverity.Informational);
        AddLog("桌面通知", "已打开 Windows 通知设置。");
    }

    [RelayCommand]
    private void OpenSystemStartupSettings()
    {
        OpenUri(WindowsStartupAppsSettingsUri);
        ShowSettingsFeedback("已打开 Windows 启动应用设置。", InfoBarSeverity.Informational);
        AddLog("窗口", "已打开 Windows 启动应用设置。");
    }

    private bool CanSendTestNtfyNotification() => !IsSendingTestNtfyNotification;

    [RelayCommand(CanExecute = nameof(CanSendTestNtfyNotification))]
    private async Task SendTestNtfyNotificationAsync()
    {
        if (!EnableNtfyNotifications)
        {
            ShowSettingsFeedback("请先启用 ntfy 推送。", InfoBarSeverity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(NtfyServerUrl) || string.IsNullOrWhiteSpace(NtfyTopic))
        {
            ShowSettingsFeedback("请填写 ntfy 服务地址和主题。", InfoBarSeverity.Warning);
            return;
        }

        IsSendingTestNtfyNotification = true;
        try
        {
            var request = new DesktopNotificationRequest(
                DesktopNotificationKind.Success,
                "E-Detection 测试推送",
                $"这是一条测试消息 · {DateTimeOffset.Now:yyyy-MM-dd HH:mm}");

            if (await _ntfyNotifications.TrySendAsync(request, this))
            {
                ShowSettingsFeedback("测试推送已发送。", InfoBarSeverity.Success);
                AddLog("消息推送", "已发送 ntfy 测试推送。");
                return;
            }

            ShowSettingsFeedback("ntfy 推送未启用或配置不完整。", InfoBarSeverity.Warning);
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   or InvalidOperationException
                                   or TaskCanceledException
                                   or UriFormatException)
        {
            ShowSettingsFeedback($"测试推送失败: {ex.Message}", InfoBarSeverity.Warning);
            AddLog("推送提醒", $"ntfy 测试推送失败: {ex.Message}");
        }
        finally
        {
            IsSendingTestNtfyNotification = false;
        }
    }

    [RelayCommand]
    private void SaveProxyPassword()
    {
        SaveSecureSecret(PendingProxyPassword, _credentials.SaveProxyPassword, () => PendingProxyPassword = "");
    }

    [RelayCommand]
    private void ClearProxyPassword()
    {
        ClearSecureSecret(_credentials.ClearProxyPassword, "代理密码已清除。");
    }

    [RelayCommand]
    private void OpenSystemProxySettings()
    {
        OpenUri(WindowsProxySettingsUri);
        ShowSettingsFeedback("已打开 Windows 代理设置。", InfoBarSeverity.Informational);
        AddLog("网络代理", "已打开 Windows 代理设置。");
    }

    private bool CanTestNetworkProxy() => !IsTestingNetworkProxy;

    [RelayCommand(CanExecute = nameof(CanTestNetworkProxy))]
    private async Task TestNetworkProxyAsync()
    {
        IsTestingNetworkProxy = true;
        try
        {
            await _networkProxy.TestProxyAsync(this);
            ShowSettingsFeedback("代理连接测试成功。", InfoBarSeverity.Success);
            AddLog("网络代理", "代理连接测试成功。");
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   or InvalidOperationException
                                   or TaskCanceledException
                                   or UriFormatException)
        {
            ShowSettingsFeedback($"代理连接测试失败: {ex.Message}", InfoBarSeverity.Warning);
            AddLog("网络代理提醒", $"代理连接测试失败: {ex.Message}");
        }
        finally
        {
            IsTestingNetworkProxy = false;
        }
    }

    private void SaveSecureSecret(
        string value,
        Action<string> save,
        Action clearInput)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ShowSettingsFeedback("请输入要保存的凭据。", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            save(value);
            clearInput();
            RefreshSecureCredentialStatus();
            ShowSettingsFeedback("凭据已保存到 Windows 凭据。", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowSettingsFeedback($"保存凭据失败: {ex.Message}", InfoBarSeverity.Warning);
        }
    }

    private void ClearSecureSecret(Action clear, string successMessage)
    {
        try
        {
            clear();
            RefreshSecureCredentialStatus();
            ShowSettingsFeedback(successMessage, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowSettingsFeedback($"清除凭据失败: {ex.Message}", InfoBarSeverity.Warning);
        }
    }

    private void RefreshSecureCredentialStatus()
    {
        LlmApiKeyStatusText = _credentials.LlmApiKeyStatusText;
        NtfyTokenStatusText = _credentials.NtfyTokenStatusText;
        ProxyPasswordStatusText = _credentials.ProxyPasswordStatusText;
    }

}
