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
    private bool CanStart() =>
        !IsRunning
        && !IsDetectionConfigDirty
        && !string.IsNullOrWhiteSpace(_activeDetectionConfigPath);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (!await EnsureReadyToStartAsync())
        {
            return;
        }

        if (IsDetectionConfigDirty)
        {
            LastFailureText = "检测阈值存在未保存修改。请先在设置中保存检测阈值。";
            StatusText = "检测阈值尚未保存";
            ShowSettingsFeedback(LastFailureText, InfoBarSeverity.Warning);
            AddLog("设置提醒", LastFailureText);
            return;
        }

        if (string.IsNullOrWhiteSpace(_activeDetectionConfigPath))
        {
            LastFailureText = "当前阈值文件未通过校验。请先加载有效配置或另存为新文件。";
            StatusText = "无法开始检测";
            ShowSettingsFeedback(LastFailureText, InfoBarSeverity.Warning);
            return;
        }

        var configPath = EnsureEffectiveConfigPath();
        ResetRunState();
        var request = new DetectionRequest
        {
            InputDirectory = InputDirectory,
            OutputDirectory = string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory,
            ConfigPath = configPath,
            WriteReport = WriteReport,
        };
        ActiveRunSummaryText = BuildActiveRunSummaryText(request);
        StartRunTelemetry();
        IsRunning = true;
        _desktopNotificationSent = false;
        StatusText = "正在准备检测...";
        SetTaskbarProgress(TaskbarProgressKind.Indeterminate, 0);
        _runCts = new CancellationTokenSource();
        SaveSettings();

        var progress = new Progress<DetectionBackendEvent>(HandleBackendEvent);

        try
        {
            var backend = DetectionBackendServiceFactory.CreateDefault();
            var exitCode = await DetectionExecutionService.RunAsync(
                backend,
                request,
                progress,
                _runCts.Token);

            if (exitCode != 0)
            {
                RunTelemetry.ApplyCurrentFile("检测失败");
                StopRunTelemetry();
                LastFailureText = $"检测组件退出码: {exitCode}";
                AddLog("错误", LastFailureText);
                StatusText = "检测失败";
                SetTaskbarProgress(TaskbarProgressKind.Error, Math.Max(ProgressPercent, 100));
                RequestDesktopNotification(
                    DesktopNotificationKind.Error,
                    "检测失败",
                    LastFailureText,
                    ReportPath);
            }
        }
        catch (OperationCanceledException)
        {
            ClearCancelConfirmation();
            RunTelemetry.ApplyCurrentFile("检测已取消");
            StopRunTelemetry();
            AddLog("取消", "检测已取消。");
            StatusText = "检测已取消";
            SetTaskbarProgress(TaskbarProgressKind.Paused, ProgressPercent);
            RequestDesktopNotification(
                DesktopNotificationKind.Warning,
                "检测已取消",
                $"已处理 {ProcessedFiles}/{TotalFiles} 个文件。",
                ReportPath);
        }
        catch (Exception ex)
        {
            ClearCancelConfirmation();
            RunTelemetry.ApplyCurrentFile("检测失败");
            StopRunTelemetry();
            LastFailureText = ex.Message;
            AddLog("错误", ex.Message);
            StatusText = "检测失败";
            SetTaskbarProgress(TaskbarProgressKind.Error, 100);
            RequestDesktopNotification(
                DesktopNotificationKind.Error,
                "检测失败",
                ex.Message,
                ReportPath);
        }
        finally
        {
            if (_runStopwatch.IsRunning)
            {
                StopRunTelemetry();
            }

            IsRunning = false;
            ClearCancelConfirmation();
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    private bool CanCancel() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (!IsCancelConfirmationPending)
        {
            ArmCancelConfirmation();
            return;
        }

        ClearCancelConfirmation(restoreRunningTaskbar: false);
        _runCts?.Cancel();
        StatusText = "正在取消...";
    }

    private void HandleBackendEvent(DetectionBackendEvent evt)
    {
        var action = _runEvents.BuildAction(evt, BuildRunEventState());
        ApplyRunEventAction(action, evt);
    }

    private void ApplyRunEventAction(
        RunEventAction action,
        DetectionBackendEvent evt)
    {
        if (action.ApplySummary)
        {
            ApplySummary(evt);
        }

        if (action.TotalFiles is { } totalFiles)
        {
            TotalFiles = totalFiles;
        }

        if (action.ProcessedFiles is { } processedFiles)
        {
            ProcessedFiles = processedFiles;
        }

        if (action.ProgressPercent is { } progressPercent)
        {
            ProgressPercent = progressPercent;
        }

        if (action.CurrentFileText is not null)
        {
            RunTelemetry.ApplyCurrentFile(action.CurrentFileText);
        }

        if (action.StatusText is not null)
        {
            StatusText = action.StatusText;
        }

        if (action.LastFailureText is not null)
        {
            LastFailureText = action.LastFailureText;
        }

        if (evt.EventName == "report_written")
        {
            ReportPath = evt.ReportPath ?? "";
        }

        if (action.ApplyReportSummary)
        {
            ApplyReportSummary(evt);
            AddLog("摘要", _runEvents.BuildReportSummaryLogMessage(BuildRunEventState()));
        }

        if (action.AddRecentReport)
        {
            AddRecentReport(ReportPath, evt);
        }

        if (action.StopTelemetry)
        {
            StopRunTelemetry();
        }
        else if (ShouldUpdateTelemetry(evt.EventName))
        {
            UpdateRunTelemetry();
        }

        if (action.TaskbarKind is { } taskbarKind)
        {
            SetTaskbarProgress(taskbarKind, action.TaskbarPercent ?? ProgressPercent);
        }

        if (action.LogKind is not null)
        {
            AddLog(action.LogKind, action.LogMessage ?? "");
        }

        if (action.ApplySummary)
        {
            AddLog("完成", _runEvents.BuildRunCompletedLogMessage(BuildRunEventState()));
            var notification = _runEvents.BuildRunCompletedNotification(BuildRunEventState(), ReportPath);
            RequestDesktopNotification(
                notification.Kind,
                notification.Title,
                notification.Message,
                notification.ReportPath);
        }

        if (action.Notification is { } notificationRequest)
        {
            RequestDesktopNotification(
                notificationRequest.Kind,
                notificationRequest.Title,
                notificationRequest.Message,
                notificationRequest.ReportPath);
        }
    }

    private static bool ShouldUpdateTelemetry(string eventName) =>
        eventName is "run_started"
            or "file_result"
            or "file_progress"
            or "report_written"
            or "report_summary";

    private RunEventState BuildRunEventState() =>
        new(
            TotalFiles,
            ProcessedFiles,
            AnomalyFiles,
            AnomalyRecords,
            SkippedFiles,
            ProgressPercent,
            HighRiskDevices.Count,
            TopIssueTypes.Count,
            SensorOfflineDevices,
            SensorFaultRows,
            SensorMissingRows,
            SensorSkippedRows);

    private void ApplySummary(DetectionBackendEvent evt)
    {
        ApplyRunSummarySnapshot(_runState.BuildSummary(evt, BuildRunSummarySnapshot()));
        if (!string.IsNullOrWhiteSpace(ReportPath))
        {
            AddRecentReport(ReportPath, evt);
        }
    }

    private RunSummarySnapshot BuildRunSummarySnapshot() =>
        new(
            TotalFiles,
            ProcessedFiles,
            AnomalyFiles,
            AnomalyRecords,
            SkippedFiles,
            ReportPath);

    private void ApplyRunSummarySnapshot(RunSummarySnapshot snapshot)
    {
        TotalFiles = snapshot.TotalFiles;
        ProcessedFiles = snapshot.ProcessedFiles;
        AnomalyFiles = snapshot.AnomalyFiles;
        AnomalyRecords = snapshot.AnomalyRecords;
        SkippedFiles = snapshot.SkippedFiles;
        ReportPath = snapshot.ReportPath;
    }

    private static string BuildActiveRunSummaryText(DetectionRequest request)
    {
        var input = FormatPathLeaf(request.InputDirectory, "未选择数据");
        var output = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? "输入目录"
            : FormatPathLeaf(request.OutputDirectory, "报告目录");
        return $"本次运行 · 数据 {input} · 报告 {output}";
    }

    private static string FormatPathLeaf(string? path, string fallback)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return fallback;
        }

        var trimmed = path.Trim().TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return fallback;
        }

        var fileName = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(fileName) ? trimmed : fileName;
    }

    private void StartRunTelemetry()
    {
        _runStopwatch.Restart();
        RunTelemetry.Start();
        OnPropertyChanged(nameof(RunTelemetryVisibility));
    }

    private void StopRunTelemetry()
    {
        UpdateRunTelemetry();
        _runStopwatch.Stop();
    }

    private void UpdateRunTelemetry()
    {
        RunTelemetry.Update(
            _runStopwatch.Elapsed,
            IsRunning,
            ProcessedFiles,
            TotalFiles,
            ProgressPercent);
    }

    private void ArmCancelConfirmation()
    {
        if (!IsRunning)
        {
            return;
        }

        _cancelPromptCts?.Cancel();
        var promptCts = new CancellationTokenSource();
        _cancelPromptCts = promptCts;
        IsCancelConfirmationPending = true;
        StatusText = "再次确认取消";
        SetTaskbarProgress(TaskbarProgressKind.Paused, ProgressPercent);
        AddLog("取消确认", "再次按 Esc 或点击确认取消停止检测。");
        _ = ClearCancelConfirmationAfterDelayAsync(promptCts);
    }

    private async Task ClearCancelConfirmationAfterDelayAsync(CancellationTokenSource promptCts)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), promptCts.Token);
            if (ReferenceEquals(_cancelPromptCts, promptCts) && IsRunning)
            {
                IsCancelConfirmationPending = false;
                _cancelPromptCts = null;
                StatusText = string.IsNullOrWhiteSpace(CurrentFileText)
                    ? "处理中..."
                    : CurrentFileText;
                SetTaskbarProgress(TaskbarProgressKind.Normal, ProgressPercent);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            promptCts.Dispose();
        }
    }

    private void ClearCancelConfirmation(bool restoreRunningTaskbar = true)
    {
        _cancelPromptCts?.Cancel();
        _cancelPromptCts = null;
        IsCancelConfirmationPending = false;
        if (restoreRunningTaskbar && IsRunning)
        {
            SetTaskbarProgress(TaskbarProgressKind.Normal, ProgressPercent);
        }
    }

    private void ResetRunState()
    {
        ClearCancelConfirmation();
        _runStopwatch.Reset();
        SelectedReport = null;
        ApplyRunSummarySnapshot(_runState.ResetSummary);
        ProgressPercent = 0;
        SetTaskbarProgress(TaskbarProgressKind.None, 0);
        ApplyReportSummarySnapshot(_runState.ResetReportSummary);
        DetailSearchText = "";
        SelectedSeverityFilterIndex = 0;
        SelectedIssueTypeFilterIndex = 0;
        SelectedDetailSortKey = "";
        DetailSortDescending = false;
        DetailIssueTypeFilters.Clear();
        DetailIssueTypeFilters.Add("全部类型");
        ActiveRunSummaryText = "";
        RunTelemetry.Reset();
        FilteredDetailPreview.Clear();
        RuntimeLogs.Clear();
        NotifyDetailPreviewChanged();
        CopyFilteredDetailsCommand.NotifyCanExecuteChanged();
    }

    private void ApplyReportSummary(DetectionBackendEvent evt)
    {
        ApplyReportSummarySnapshot(_runState.BuildReportSummary(evt));
        RefreshDetailPreview();
        SelectFirstVisibleDetailIfAvailable();
    }

    private void ApplyReportSummarySnapshot(ReportSummarySnapshot snapshot)
    {
        ReportDeviceCount = snapshot.DeviceCount;
        HighRiskDevices.Clear();
        foreach (var device in snapshot.HighRiskDevices)
        {
            HighRiskDevices.Add(device);
        }

        TopIssueTypes.Clear();
        foreach (var issue in snapshot.TopIssueTypes)
        {
            TopIssueTypes.Add(issue);
        }

        SensorOfflineDevices = snapshot.SensorOverview.OfflineDevices;
        SensorFaultRows = snapshot.SensorOverview.SensorFaultRows;
        SensorMissingRows = snapshot.SensorOverview.SensorMissingRows;
        SensorSkippedRows = snapshot.SensorOverview.SkippedRows;

        DetailPreview.Clear();
        foreach (var detail in snapshot.DetailPreview)
        {
            DetailPreview.Add(detail);
        }

        DetailPreviewTotalCount = snapshot.DetailPreviewCount;
    }

    private void RefreshDetailPreview()
    {
        IEnumerable<ReportDetailPreview> source = SelectedReport is { } report
            ? report.DetailPreview
            : DetailPreview;
        var result = _detailPreview.Refresh(
            source,
            SelectedDetail,
            DetailIssueTypeFilters,
            BuildDetailFilterState());
        DetailIssueTypeFilters.Clear();
        foreach (var issueType in result.IssueTypeFilters)
        {
            DetailIssueTypeFilters.Add(issueType);
        }

        if (SelectedIssueTypeFilterIndex != result.SelectedIssueTypeFilterIndex)
        {
            SelectedIssueTypeFilterIndex = result.SelectedIssueTypeFilterIndex;
        }

        FilteredDetailPreview.Clear();
        foreach (var detail in result.FilteredDetails)
        {
            FilteredDetailPreview.Add(detail);
        }

        if (!ReferenceEquals(SelectedDetail, result.SelectedDetail))
        {
            SelectedDetail = result.SelectedDetail;
        }

        NotifyDetailPreviewChanged();
        CopyFilteredDetailsCommand.NotifyCanExecuteChanged();
        OpenFirstAnomalySourceCommand.NotifyCanExecuteChanged();
    }

    private void NotifyDetailPreviewChanged()
    {
        OnPropertyChanged(nameof(DetailPreviewStatusText));
        OnPropertyChanged(nameof(WorkbenchFilteredDetailPreview));
        OnPropertyChanged(nameof(WorkbenchDetailPreviewStatusText));
        OnPropertyChanged(nameof(DetailSortStatusText));
        OnPropertyChanged(nameof(DetailTableEmptyStateTitle));
        OnPropertyChanged(nameof(DetailTableEmptyStateHint));
        OnPropertyChanged(nameof(DetailTableEmptyStateVisibility));
    }

    private void SelectFirstVisibleDetailIfAvailable()
    {
        if (SelectedDetail is null && FilteredDetailPreview.Count > 0)
        {
            SelectedDetail = FilteredDetailPreview[0];
        }
    }

    private ReportDetailFilterState BuildDetailFilterState() =>
        new(
            DetailSearchText,
            SelectedSeverityFilterIndex,
            SelectedIssueTypeFilterIndex,
            SelectedDetailSortKey,
            DetailSortDescending);

    private static string SensorOverviewTextFrom(ReportSensorOverview sensor) =>
        $"离线 {sensor.OfflineDevices} · 传感器故障 {sensor.SensorFaultRows} · 未配置 {sensor.SensorMissingRows} · 跳过 {sensor.SkippedRows}";

    private void RefreshReportHistory()
    {
        ReportHistory.SelectedReport = SelectedReport;
        ReportHistory.Refresh();
        OnPropertyChanged(nameof(ReportHistoryStatusText));
    }

    private void NotifyWorkbenchSnapshotChanged()
    {
        OnPropertyChanged(nameof(IsShowingReportSnapshot));
        OnPropertyChanged(nameof(WorkbenchModeText));
        OnPropertyChanged(nameof(WorkbenchStatusText));
        OnPropertyChanged(nameof(WorkbenchProgressText));
        OnPropertyChanged(nameof(WorkbenchProgressPercent));
        OnPropertyChanged(nameof(WorkbenchProcessedSummary));
        OnPropertyChanged(nameof(WorkbenchAnomalyFiles));
        OnPropertyChanged(nameof(WorkbenchAnomalyRecords));
        OnPropertyChanged(nameof(WorkbenchSkippedFiles));
        OnPropertyChanged(nameof(WorkbenchReportPath));
        OnPropertyChanged(nameof(WorkbenchReportPathText));
        OnPropertyChanged(nameof(WorkbenchReportButtonText));
        OnPropertyChanged(nameof(WorkbenchReportFolderButtonText));
        OnPropertyChanged(nameof(WorkbenchContextText));
        OnPropertyChanged(nameof(WorkbenchReportDeviceCount));
        OnPropertyChanged(nameof(WorkbenchHighRiskDevices));
        OnPropertyChanged(nameof(WorkbenchTopIssueTypes));
        OnPropertyChanged(nameof(WorkbenchSensorOverviewText));
        OnPropertyChanged(nameof(WorkbenchDetailPreviewTotalCount));
        OnPropertyChanged(nameof(WorkbenchFilteredDetailPreview));
        OnPropertyChanged(nameof(WorkbenchDetailPreviewStatusText));
        RefreshFirstRunGuide();
        OpenCurrentReportCommand.NotifyCanExecuteChanged();
        OpenCurrentReportFolderCommand.NotifyCanExecuteChanged();
        CopyCurrentReportPathCommand.NotifyCanExecuteChanged();
        CopyFilteredDetailsCommand.NotifyCanExecuteChanged();
        CopyCompletionSummaryCommand.NotifyCanExecuteChanged();
        OpenFirstAnomalySourceCommand.NotifyCanExecuteChanged();
    }

    private bool CanShowCurrentRun() => SelectedReport is not null;

    [RelayCommand(CanExecute = nameof(CanShowCurrentRun))]
    private void ShowCurrentRun()
    {
        SelectedReport = null;
        AddLog("视图", "已返回当前检测状态。");
    }

    private bool CanCopyCompletionSummary() => ShouldShowCompletionActions;

    [RelayCommand(CanExecute = nameof(CanCopyCompletionSummary))]
    private void CopyCompletionSummary()
    {
        CopyTextToClipboard(BuildCompletionSummaryText());
        AddLog("复制", "已复制检测完成摘要。");
    }

}
