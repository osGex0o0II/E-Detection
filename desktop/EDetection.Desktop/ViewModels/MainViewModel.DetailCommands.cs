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
    private bool CanOpenFirstAnomalySource()
    {
        var detail = WorkbenchFilteredDetailPreview.FirstOrDefault();
        return detail is not null && File.Exists(ResolveDetailSourcePath(detail));
    }

    [RelayCommand(CanExecute = nameof(CanOpenFirstAnomalySource))]
    private void OpenFirstAnomalySource()
    {
        var detail = WorkbenchFilteredDetailPreview.FirstOrDefault();
        if (detail is null)
        {
            return;
        }

        SelectedDetail = detail;
        var path = ResolveDetailSourcePath(detail);
        if (!File.Exists(path))
        {
            return;
        }

        OpenContainingFolder(path);
        AddLog("定位", "已打开首个异常的源文件位置。");
    }

    private void AddLog(string kind, string message)
    {
        RuntimeLogs.Add(DiagnosticsRedactor.Redact(kind), DiagnosticsRedactor.Redact(message));
    }

    public void AddRuntimeLog(string kind, string message) => AddLog(kind, message);

    public string BuildLogExportText()
    {
        return RuntimeLogs.BuildExportText();
    }

    private bool CanCopyFilteredLogs() => FilteredLogItems.Count > 0;

    [RelayCommand(CanExecute = nameof(CanCopyFilteredLogs))]
    private void CopyFilteredLogs()
    {
        if (FilteredLogItems.Count == 0)
        {
            return;
        }

        CopyTextToClipboard(BuildLogExportText());
    }

    [RelayCommand]
    private void ClearLogFilters()
    {
        RuntimeLogs.ClearFilters();
    }

    private bool CanClearRuntimeLogs() => LogItems.Count > 0;

    [RelayCommand(CanExecute = nameof(CanClearRuntimeLogs))]
    private void ClearRuntimeLogs()
    {
        RuntimeLogs.Clear();
        ClearRuntimeLogsCommand.NotifyCanExecuteChanged();
        CopyFilteredLogsCommand.NotifyCanExecuteChanged();
    }

    private Task<bool> EnsureReadyToStartAsync()
    {
        RefreshLocalDiagnostics();
        var issues = _diagnostics.ValidateRunInputs(BuildDiagnosticsRequest(), prepareOutputDirectory: true);
        if (issues.Count > 0)
        {
            BlockStart(issues);
            return Task.FromResult(false);
        }

        LastFailureText = "暂无失败";
        AddLog("状态检查", "Native .NET 检测核心已就绪。");
        return Task.FromResult(true);
    }

    [RelayCommand]
    private void RefreshDiagnostics()
    {
        RefreshLocalDiagnostics();
        AddLog("状态检查", "Native .NET 检测核心已就绪。");
    }

    [RelayCommand]
    private void RefreshDesktopHealth() => DesktopHealth.Refresh();

    private void RefreshLocalDiagnostics()
    {
        var snapshot = _diagnostics.BuildLocalSnapshot(BuildDiagnosticsRequest());
        Diagnostics.ApplyLocalSnapshot(snapshot);
        RefreshFirstRunGuide();
    }

    private DesktopDiagnosticsRequest BuildDiagnosticsRequest() =>
        new(
            InputDirectory,
            OutputDirectory,
            DetectionConfigService.ResolveEffectiveConfigPath(ConfigPath),
            WriteReport);

    private void RefreshFirstRunGuide()
    {
        OnPropertyChanged(nameof(FirstRunGuideVisibility));
        OnPropertyChanged(nameof(FirstRunGuideTitleText));
        OnPropertyChanged(nameof(FirstRunGuideSubtitleText));
        OnPropertyChanged(nameof(FirstRunInputStepText));
        OnPropertyChanged(nameof(FirstRunConfigStepText));
        OnPropertyChanged(nameof(FirstRunBackendStepText));
        OnPropertyChanged(nameof(FirstRunOutputStepText));
        OnPropertyChanged(nameof(RunSetupReadinessText));
        OnPropertyChanged(nameof(RunSetupReadinessDetailText));
    }

    private void BlockStart(IReadOnlyList<string> issues)
    {
        var message = string.Join("；", issues);
        var action = DesktopDiagnosticsService.BuildBlockStartAction(issues);
        BlockStart(message, action);
    }

    private void BlockStart(string message, string action)
    {
        StatusText = "无法开始检测";
        LastFailureText = message;
        Diagnostics.ApplyBlockStart(message, action);
        SetTaskbarProgress(TaskbarProgressKind.None, 0);
        AddLog("就绪检查", message);
    }

    private bool IsLocalInputReady() =>
        DesktopDiagnosticsService.IsInputReady(InputDirectory);

    private bool IsConfigReady()
        => DetectionConfigService.ValidateConfigFile(
            DetectionConfigService.ResolveEffectiveConfigPath(ConfigPath)) is null;

    private bool CanOpenUpdateFeed() => !IsCheckingForUpdates && !IsDownloadingUpdateInstaller;

    [RelayCommand(CanExecute = nameof(CanOpenUpdateFeed))]
    private async Task OpenUpdateFeedAsync()
    {
        if (string.IsNullOrWhiteSpace(UpdateFeedUrl))
        {
            ShowSettingsFeedback("请先填写更新源。", InfoBarSeverity.Warning);
            return;
        }

        var result = await CheckForUpdatesCoreAsync(showProgress: true);
        if (result is { IsUpdateAvailable: false })
        {
            ShowSettingsFeedback("当前已是最新版本。", InfoBarSeverity.Informational);
            return;
        }

        if (result is { IsUpdateAvailable: true, HasInstallerDownload: true }
            && IsOfficialReleaseUrl(result.ReleaseUrl))
        {
            await DownloadAndRevealUpdateInstallerAsync(result);
            return;
        }

        if (result is { IsUpdateAvailable: true } && !IsOfficialReleaseUrl(result.ReleaseUrl))
        {
            ShowSettingsFeedback("检测到非官方更新源返回新版本。为安全起见，请手动确认发布页面。", InfoBarSeverity.Warning);
        }

        var targetUrl = result?.PreferredActionUrl;
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            targetUrl = GetPreferredUpdateActionUrl();
        }

        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            ShowSettingsFeedback("未找到可用的更新地址。", InfoBarSeverity.Warning);
            return;
        }

        if (!IsOfficialReleaseUrl(targetUrl))
        {
            ShowSettingsFeedback("非官方更新地址不会自动打开，请确认更新源后手动访问。", InfoBarSeverity.Warning);
            AddLog("更新提醒", $"已阻止自动打开非官方更新地址: {targetUrl}");
            return;
        }

        OpenUri(targetUrl);
    }

    private bool CanCheckForUpdates() => !IsCheckingForUpdates && !IsDownloadingUpdateInstaller;

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        await CheckForUpdatesCoreAsync(showProgress: true);
    }

    public async Task CheckForUpdatesInBackgroundAsync()
    {
        if (!ShouldCheckForUpdatesOnStartup || IsCheckingForUpdates)
        {
            return;
        }

        var result = await CheckForUpdatesCoreAsync(showProgress: false);
        NotifyUpdateAvailableIfNeeded(result);
    }

    private async Task<UpdateCheckResult?> CheckForUpdatesCoreAsync(bool showProgress)
    {
        IsCheckingForUpdates = true;
        if (showProgress)
        {
            UpdateStatusText = "正在检查更新...";
        }

        try
        {
            var currentVersion = new AppInfoService().GetInfo().Version;
            using var handler = _networkProxy.BuildHandler(this, UseProxyForUpdates);
            var result = await _updateCheck.CheckLatestAsync(UpdateFeedUrl, currentVersion, handler);
            LatestReleaseUrl = result.ReleaseUrl;
            LatestInstallerName = result.InstallerName;
            LatestInstallerDownloadUrl = result.InstallerDownloadUrl;
            var publishedText = result.PublishedAt is { } publishedAt
                ? $" · {publishedAt:yyyy-MM-dd}"
                : "";
            var installerText = result.HasInstallerDownload
                ? " · 可下载安装向导"
                : " · 打开发布页面获取安装包";
            UpdateStatusText = result.IsUpdateAvailable
                ? $"发现新版本 {result.LatestVersion}{publishedText}{installerText}"
                : $"已是最新版本 {currentVersion}";
            AddLog("更新", result.IsUpdateAvailable
                ? BuildUpdateAvailableLogText(result)
                : $"已是最新版本 {currentVersion}");
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   or JsonException
                                   or ArgumentException
                                   or InvalidOperationException
                                   or TaskCanceledException)
        {
            UpdateStatusText = showProgress
                ? $"检查更新失败: {ex.Message}"
                : "自动检查更新失败";
            AddLog("更新提醒", showProgress ? UpdateStatusText : $"自动检查更新失败: {ex.Message}");
            return null;
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private void NotifyUpdateAvailableIfNeeded(UpdateCheckResult? result)
    {
        if (result is not { IsUpdateAvailable: true })
        {
            return;
        }

        if (string.Equals(_lastNotifiedUpdateVersion, result.LatestVersion, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!EnableDesktopNotifications)
        {
            return;
        }

        if (!IsOfficialReleaseUrl(result.ReleaseUrl))
        {
            AddLog("更新提醒", $"非官方更新源发现版本 {result.LatestVersion}，已跳过桌面通知跳转。");
            return;
        }

        _lastNotifiedUpdateVersion = result.LatestVersion;
        var targetUrl = string.IsNullOrWhiteSpace(result.ReleaseUrl)
            ? UpdateFeedUrl
            : result.ReleaseUrl;
        var message = result.HasInstallerDownload
            ? $"可更新到 {result.LatestVersion} · 打开发布页面获取安装向导"
            : $"可更新到 {result.LatestVersion} · 打开发布页面获取安装包";
        DesktopNotificationRequested?.Invoke(
            this,
            new DesktopNotificationRequest(
                DesktopNotificationKind.Update,
                "发现 E-Detection 新版本",
                message,
                ActionUrl: targetUrl));
    }

    private string GetPreferredUpdateActionUrl()
    {
        if (!string.IsNullOrWhiteSpace(LatestInstallerDownloadUrl))
        {
            return LatestInstallerDownloadUrl;
        }

        if (!string.IsNullOrWhiteSpace(LatestReleaseUrl))
        {
            return LatestReleaseUrl;
        }

        return UpdateFeedUrl;
    }

    private async Task DownloadAndRevealUpdateInstallerAsync(UpdateCheckResult result)
    {
        IsDownloadingUpdateInstaller = true;
        UpdateStatusText = $"正在下载安装向导 {result.LatestVersion}...";
        try
        {
            var installerPath = await DownloadUpdateInstallerAsync(result);
            UpdateStatusText = $"安装向导已下载 {result.LatestVersion}";
            AddLog("更新", $"已下载更新安装向导: {installerPath}");
            OpenContainingFolder(installerPath);
            ShowSettingsFeedback(
                "安装向导已下载。SHA-256 仅验证文件完整性，不能验证发布者身份；如安装包未签名，Windows 可能显示“未知发布者”。请核对 Release 说明，再手动运行安装向导完成更新。",
                InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   or IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or InvalidOperationException
                                   or NotSupportedException
                                   or TaskCanceledException
                                   or Win32Exception)
        {
            UpdateStatusText = $"下载安装向导失败: {ex.Message}";
            AddLog("更新提醒", UpdateStatusText);
            var fallbackUrl = string.IsNullOrWhiteSpace(result.ReleaseUrl)
                ? UpdateFeedUrl
                : result.ReleaseUrl;
            if (!string.IsNullOrWhiteSpace(fallbackUrl))
            {
                OpenUri(fallbackUrl);
            }
        }
        finally
        {
            IsDownloadingUpdateInstaller = false;
        }
    }

    private async Task<string> DownloadUpdateInstallerAsync(UpdateCheckResult result)
    {
        if (string.IsNullOrWhiteSpace(result.InstallerDownloadUrl))
        {
            throw new InvalidOperationException("更新源未提供安装向导下载地址。");
        }

        EnsureTrustedUpdateInstallerSource(result);

        var fileName = BuildUpdateInstallerFileName(result.InstallerName, result.LatestVersion);
        var updatesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "E-Detection",
            "Desktop",
            "Updates",
            result.LatestVersion);
        Directory.CreateDirectory(updatesDirectory);
        var installerPath = Path.Combine(updatesDirectory, fileName);
        var partialPath = installerPath + ".download";

        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }

        using var handler = _networkProxy.BuildHandler(this, UseProxyForUpdates);
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("E-Detection-Desktop");

        using var response = await client.GetAsync(
            result.InstallerDownloadUrl,
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using (var source = await response.Content.ReadAsStreamAsync())
        await using (var target = File.Create(partialPath))
        {
            await source.CopyToAsync(target);
        }

        var downloadedFile = new FileInfo(partialPath);
        if (!downloadedFile.Exists || downloadedFile.Length == 0)
        {
            throw new InvalidOperationException("安装向导下载为空。");
        }

        await VerifyUpdateInstallerDigestAsync(partialPath, result, client);

        if (File.Exists(installerPath))
        {
            File.Delete(installerPath);
        }

        File.Move(partialPath, installerPath);
        return installerPath;
    }

    private static void EnsureTrustedUpdateInstallerSource(UpdateCheckResult result)
    {
        if (!Uri.TryCreate(result.ReleaseUrl, UriKind.Absolute, out var releaseUri)
            || !Uri.TryCreate(result.InstallerDownloadUrl, UriKind.Absolute, out var installerUri)
            || releaseUri.Scheme != Uri.UriSchemeHttps
            || installerUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("更新安装向导必须来自 HTTPS 官方发布页。");
        }

        var releaseUrl = releaseUri.ToString();
        var installerUrl = installerUri.ToString();
        if (!releaseUrl.StartsWith(OfficialReleaseBaseUrl, StringComparison.OrdinalIgnoreCase)
            || !installerUrl.StartsWith(OfficialReleaseBaseUrl, StringComparison.OrdinalIgnoreCase)
            || !installerUri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("为安全起见，只会下载官方 GitHub Release 中的安装向导。");
        }

        if (!string.IsNullOrWhiteSpace(result.ChecksumDownloadUrl))
        {
            if (!Uri.TryCreate(result.ChecksumDownloadUrl, UriKind.Absolute, out var checksumUri)
                || checksumUri.Scheme != Uri.UriSchemeHttps
                || !checksumUri.ToString().StartsWith(OfficialReleaseBaseUrl, StringComparison.OrdinalIgnoreCase)
                || !checksumUri.AbsolutePath.EndsWith(".sha256.txt", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("更新校验文件必须来自官方 GitHub Release。");
            }
        }
    }

    private static bool IsOfficialReleaseUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.ToString().StartsWith(OfficialReleaseBaseUrl, StringComparison.OrdinalIgnoreCase);

    private static async Task VerifyUpdateInstallerDigestAsync(
        string installerPath,
        UpdateCheckResult result,
        HttpClient client)
    {
        var expected = TryGetSha256FromDigest(result.InstallerDigest);
        if (string.IsNullOrWhiteSpace(expected)
            && !string.IsNullOrWhiteSpace(result.ChecksumDownloadUrl))
        {
            expected = await DownloadExpectedSha256FromChecksumAsync(result, client);
        }

        if (string.IsNullOrWhiteSpace(expected))
        {
            throw new InvalidOperationException("更新源未提供安装包 SHA-256 摘要，已改为打开发布页面。");
        }

        using var stream = File.OpenRead(installerPath);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actual, expected.ToLowerInvariant(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("安装向导摘要校验失败，已阻止启动该更新。");
        }
    }

    private static string TryGetSha256FromDigest(string digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return "";
        }

        const string prefix = "sha256:";
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        var expected = digest[prefix.Length..].Trim();
        return IsSha256Hex(expected) ? expected : "";
    }

    private static async Task<string> DownloadExpectedSha256FromChecksumAsync(
        UpdateCheckResult result,
        HttpClient client)
    {
        using var response = await client.GetAsync(result.ChecksumDownloadUrl);
        response.EnsureSuccessStatusCode();
        var checksumText = await response.Content.ReadAsStringAsync();
        foreach (var rawLine in checksumText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length < 66)
            {
                continue;
            }

            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var hash = parts[0].Trim();
            var fileName = Path.GetFileName(parts[^1].TrimStart('*'));
            if (IsSha256Hex(hash)
                && string.Equals(fileName, result.InstallerName, StringComparison.OrdinalIgnoreCase))
            {
                return hash;
            }
        }

        throw new InvalidOperationException("更新校验文件中未找到安装向导的 SHA-256 摘要。");
    }

    private static bool IsSha256Hex(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string BuildUpdateInstallerFileName(string assetName, string latestVersion)
    {
        var fallback = $"EDetection-Setup-win-x64-{latestVersion}.exe";
        var fileName = Path.GetFileName(string.IsNullOrWhiteSpace(assetName) ? fallback : assetName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = fallback;
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.exe";
    }

    private static string BuildUpdateAvailableLogText(UpdateCheckResult result)
    {
        var installerText = result.HasInstallerDownload
            ? $"，安装向导: {result.InstallerName}"
            : "，未在发布产物中找到安装向导";
        return $"发现新版本 {result.LatestVersion}: {result.ReleaseName}{installerText}";
    }

    [RelayCommand]
    private void CopyDiagnostics()
    {
        CopyTextToClipboard(Diagnostics.BuildClipboardText());
        AddLog("状态检查", "已复制脱敏诊断信息。");
    }

    private bool CanCopySelectedDetail() => SelectedDetail is not null;

    [RelayCommand(CanExecute = nameof(CanCopySelectedDetail))]
    private void CopySelectedDetail()
    {
        if (SelectedDetail is null)
        {
            return;
        }

        CopyTextToClipboard(SelectedDetail.ToClipboardText());
        AddLog("复制", "已复制选中的异常明细。");
    }

    private bool CanExplainSelectedDetail() =>
        SelectedDetail is not null && !IsExplainingSelectedDetail;

    [RelayCommand(CanExecute = nameof(CanExplainSelectedDetail))]
    private async Task ExplainSelectedDetailAsync()
    {
        if (SelectedDetail is not { } detail)
        {
            return;
        }

        IsExplainingSelectedDetail = true;
        LlmDetailExplanationText = "正在生成智能解读...";
        try
        {
            var response = await _llmAssistant.ExplainDetailAsync(this, detail.ToClipboardText());
            if (ReferenceEquals(SelectedDetail, detail))
            {
                LlmDetailExplanationText = string.IsNullOrWhiteSpace(response)
                    ? "智能助手已响应，但未返回可显示的解读。"
                    : response;
            }
            AddLog("智能助手", "已生成选中异常明细解读。");
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   or InvalidOperationException
                                   or TaskCanceledException
                                   or UriFormatException
                                   or JsonException)
        {
            LlmDetailExplanationText = $"智能解读失败: {ex.Message}";
            AddLog("智能助手提醒", $"异常明细解读失败: {ex.Message}");
        }
        finally
        {
            IsExplainingSelectedDetail = false;
        }
    }

    private bool CanOpenSelectedDetailSource() =>
        SelectedDetail is not null && File.Exists(ResolveSelectedDetailSourcePath());

    [RelayCommand(CanExecute = nameof(CanOpenSelectedDetailSource))]
    private void OpenSelectedDetailSource()
    {
        var path = ResolveSelectedDetailSourcePath();
        if (!File.Exists(path))
        {
            return;
        }

        OpenContainingFolder(path);
        AddLog("定位", "已打开选中异常的源文件位置。");
    }

    private bool CanCopySelectedDetailSourcePath() =>
        SelectedDetail is not null && !string.IsNullOrWhiteSpace(ResolveSelectedDetailSourcePath());

    [RelayCommand(CanExecute = nameof(CanCopySelectedDetailSourcePath))]
    private void CopySelectedDetailSourcePath()
    {
        var path = ResolveSelectedDetailSourcePath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        CopyTextToClipboard(path);
        AddLog("复制", "已复制源文件路径。");
    }

    private string ResolveSelectedDetailSourcePath()
    {
        if (SelectedDetail is not { } detail)
        {
            return "";
        }

        return ResolveDetailSourcePath(detail);
    }

    private string ResolveDetailSourcePath(ReportDetailPreview detail)
    {
        var rawPath = !string.IsNullOrWhiteSpace(detail.RelativePath)
            ? detail.RelativePath
            : detail.SourceFile;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return "";
        }

        if (Path.IsPathRooted(rawPath))
        {
            return "";
        }

        var inputRoot = SelectedReport?.InputDirectory ?? InputDirectory;
        if (string.IsNullOrWhiteSpace(inputRoot))
        {
            return "";
        }

        try
        {
            var rootFullPath = Path.GetFullPath(inputRoot);
            var candidatePath = Path.GetFullPath(Path.Combine(rootFullPath, rawPath));
            var normalizedRoot = rootFullPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return candidatePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                ? candidatePath
                : "";
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or NotSupportedException
                                   or PathTooLongException)
        {
            return "";
        }
    }

    private bool CanCopyFilteredDetails() => WorkbenchFilteredDetailPreview.Any();

    [RelayCommand(CanExecute = nameof(CanCopyFilteredDetails))]
    private void CopyFilteredDetails()
    {
        var rows = WorkbenchFilteredDetailPreview.ToList();
        if (rows.Count == 0)
        {
            return;
        }

        CopyTextToClipboard(_detailPreview.BuildExportText(rows));
        AddLog("复制", $"已复制 {rows.Count} 条异常明细。");
    }

    [RelayCommand]
    private void ClearDetailFilters()
    {
        DetailSearchText = "";
        SelectedSeverityFilterIndex = 0;
        SelectedIssueTypeFilterIndex = 0;
        SelectedDetailSortKey = "";
    }

    [RelayCommand]
    private void SortDetails(string key)
    {
        SelectedDetailSortKey = string.Equals(SelectedDetailSortKey, key, StringComparison.Ordinal)
            ? ""
            : key;
    }

}
