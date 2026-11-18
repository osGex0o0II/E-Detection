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
    private void UpdateStartupIntegrationStatus(StartupIntegrationSnapshot status)
    {
        StartupIntegrationStatusText = status.StatusText;
    }

    private void AddRecentReport(string path, DetectionBackendEvent? evt)
    {
        var added = ReportHistory.AddOrUpdate(
            path,
            evt,
            BuildRecentReportUpdateContext());
        if (!added)
        {
            return;
        }

        NotifyWorkbenchSnapshotChanged();
        ClearRecentReportsCommand.NotifyCanExecuteChanged();
        SaveSettings();
    }

    private RecentReportUpdateContext BuildRecentReportUpdateContext() =>
        new(
            InputDirectory,
            OutputDirectory,
            TotalFiles,
            ProcessedFiles,
            AnomalyRecords,
            AnomalyFiles,
            SkippedFiles,
            ReportDeviceCount,
            HighRiskDevices.ToList(),
            TopIssueTypes.ToList(),
            new ReportSensorOverview
            {
                OfflineDevices = SensorOfflineDevices,
                SensorFaultRows = SensorFaultRows,
                SensorMissingRows = SensorMissingRows,
                SkippedRows = SensorSkippedRows,
            },
            DetailPreviewTotalCount,
            DetailPreview.ToList());

    private bool CanOpenCurrentReport() =>
        !string.IsNullOrWhiteSpace(WorkbenchReportPath) && File.Exists(WorkbenchReportPath);

    [RelayCommand(CanExecute = nameof(CanOpenCurrentReport))]
    private void OpenCurrentReport() => OpenPath(WorkbenchReportPath);

    [RelayCommand(CanExecute = nameof(CanOpenCurrentReport))]
    private void OpenCurrentReportFolder() => OpenContainingFolder(WorkbenchReportPath);

    private bool CanCopyCurrentReportPath() =>
        !string.IsNullOrWhiteSpace(WorkbenchReportPath);

    [RelayCommand(CanExecute = nameof(CanCopyCurrentReportPath))]
    private void CopyCurrentReportPath()
    {
        CopyTextToClipboard(WorkbenchReportPath);
        AddLog("复制", "已复制报告路径。");
    }

    private bool CanOpenSelectedReport() =>
        SelectedReport is not null && File.Exists(SelectedReport.Path);

    [RelayCommand(CanExecute = nameof(CanOpenSelectedReport))]
    private void OpenSelectedReport()
    {
        if (SelectedReport is not null)
        {
            OpenPath(SelectedReport.Path);
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelectedReport))]
    private void OpenSelectedReportFolder()
    {
        if (SelectedReport is not null)
        {
            OpenContainingFolder(SelectedReport.Path);
        }
    }

    private bool CanUseSelectedReportDirectories() =>
        !IsRunning && SelectedReport is not null && !string.IsNullOrWhiteSpace(SelectedReport.InputDirectory);

    [RelayCommand(CanExecute = nameof(CanUseSelectedReportDirectories))]
    private void UseSelectedReportDirectories()
    {
        if (SelectedReport is null)
        {
            return;
        }

        InputDirectory = SelectedReport.InputDirectory;
        if (!string.IsNullOrWhiteSpace(SelectedReport.OutputDirectory))
        {
            OutputDirectory = SelectedReport.OutputDirectory;
        }

        StatusText = $"已复用路径: {SelectedReport.FileName}";
        SaveSettings();
    }

    private bool CanRemoveSelectedReport() => SelectedReport is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedReport))]
    private void RemoveSelectedReport()
    {
        if (!ReportHistory.RemoveSelected())
        {
            return;
        }

        NotifyWorkbenchSnapshotChanged();
        ClearRecentReportsCommand.NotifyCanExecuteChanged();
        SaveSettings();
    }

    private bool CanClearRecentReports() => RecentReports.Count > 0;

    [RelayCommand(CanExecute = nameof(CanClearRecentReports))]
    private void ClearRecentReports()
    {
        ReportHistory.Clear();
        NotifyWorkbenchSnapshotChanged();
        ClearRecentReportsCommand.NotifyCanExecuteChanged();
        SaveSettings();
    }

    private static void OpenPath(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void OpenContainingFolder(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { "/select,", path },
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private bool OpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowSettingsFeedback($"打开外部链接失败: {ex.Message}", InfoBarSeverity.Warning);
            return false;
        }
    }
}
