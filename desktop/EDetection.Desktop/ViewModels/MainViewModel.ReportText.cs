using CommunityToolkit.Mvvm.ComponentModel;
using EDetection.Desktop.Models;
using EDetection.Desktop.Services;

namespace EDetection.Desktop.ViewModels;

public partial class MainViewModel
    : ObservableObject,
        INetworkProxySettings,
        INtfyNotificationSettings,
        ILlmAssistantSettings
{
    private string BuildCompletionSummaryText() =>
        _reportSummaryComposer.BuildCompletionSummaryText(new ReportSummaryContext
        {
            InputDirectory = InputDirectory,
            DetailPreview = DetailPreview.ToList(),
            HighRiskDevices = HighRiskDevices.ToList(),
            TopIssueTypes = TopIssueTypes.ToList(),
            TotalFiles = TotalFiles,
            AnomalyFiles = AnomalyFiles,
            AnomalyRecords = AnomalyRecords,
            SkippedFiles = SkippedFiles,
            ReportDeviceCount = ReportDeviceCount,
            SensorOfflineDevices = SensorOfflineDevices,
            SensorFaultRows = SensorFaultRows,
            SensorMissingRows = SensorMissingRows,
            SensorSkippedRows = SensorSkippedRows,
            ProcessedSummary = ProcessedSummary,
            RunElapsedText = RunElapsedText,
            ReportPath = ReportPath,
        });
}
