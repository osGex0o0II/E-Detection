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
    private void ReportHistory_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReportHistoryViewModel.SelectedReport)
            && !ReferenceEquals(SelectedReport, ReportHistory.SelectedReport))
        {
            SelectedReport = ReportHistory.SelectedReport;
        }

        if (e.PropertyName is nameof(ReportHistoryViewModel.StatusText))
        {
            OnPropertyChanged(nameof(ReportHistoryStatusText));
            OnPropertyChanged(nameof(WorkbenchStatusBadgeText));
        }

        if (e.PropertyName is nameof(ReportHistoryViewModel.RecentReportLimit)
            or nameof(ReportHistoryViewModel.RecentReportLimitText))
        {
            ClearRecentReportsCommand.NotifyCanExecuteChanged();
            SavePreferenceSettings();
        }
    }

    private void Diagnostics_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DiagnosticsViewModel.SummaryText):
                OnPropertyChanged(nameof(RunSetupReadinessText));
                OnPropertyChanged(nameof(RunSetupReadinessDetailText));
                break;
            case nameof(DiagnosticsViewModel.BackendText):
                OnPropertyChanged(nameof(BackendDiagnosticText));
                break;
            case nameof(DiagnosticsViewModel.ConfigText):
                OnPropertyChanged(nameof(ConfigDiagnosticText));
                break;
            case nameof(DiagnosticsViewModel.InputText):
                OnPropertyChanged(nameof(InputDiagnosticText));
                break;
            case nameof(DiagnosticsViewModel.OutputText):
                OnPropertyChanged(nameof(OutputDiagnosticText));
                break;
            case nameof(DiagnosticsViewModel.ActionText):
                OnPropertyChanged(nameof(DiagnosticActionText));
                OnPropertyChanged(nameof(RunSetupReadinessDetailText));
                break;
        }
    }

    private void RunTelemetry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RunTelemetryViewModel.CurrentFileText):
                OnPropertyChanged(nameof(CurrentFileText));
                OnPropertyChanged(nameof(WorkbenchStatusBadgeText));
                OnPropertyChanged(nameof(RunSetupReadinessDetailText));
                OnPropertyChanged(nameof(RunTelemetryVisibility));
                break;
            case nameof(RunTelemetryViewModel.ElapsedText):
                OnPropertyChanged(nameof(RunElapsedText));
                break;
            case nameof(RunTelemetryViewModel.SpeedText):
                OnPropertyChanged(nameof(RunSpeedText));
                break;
            case nameof(RunTelemetryViewModel.RemainingText):
                OnPropertyChanged(nameof(RunRemainingText));
                break;
            case nameof(RunTelemetryViewModel.ProgressDetailText):
                OnPropertyChanged(nameof(RunProgressDetailText));
                break;
        }
    }

    private void RuntimeLogs_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RuntimeLogViewModel.StatusText):
                ClearRuntimeLogsCommand.NotifyCanExecuteChanged();
                CopyFilteredLogsCommand.NotifyCanExecuteChanged();
                break;
            case nameof(RuntimeLogViewModel.RetentionLimit):
            case nameof(RuntimeLogViewModel.RetentionText):
                SavePreferenceSettings();
                break;
            case nameof(RuntimeLogViewModel.SelectedRetentionIndex):
                OnPropertyChanged(nameof(SelectedLogRetentionIndex));
                break;
            case nameof(RuntimeLogViewModel.SearchText):
                OnPropertyChanged(nameof(LogSearchText));
                break;
            case nameof(RuntimeLogViewModel.SelectedKindFilterIndex):
                OnPropertyChanged(nameof(SelectedLogKindFilterIndex));
                break;
        }
    }

}
