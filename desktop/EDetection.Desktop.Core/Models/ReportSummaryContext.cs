using EDetection.NativeCore.Models;

namespace EDetection.Desktop.Models;

/// <summary>
/// Snapshot of the values the report-summary composer needs.  Keeping this
/// separate from <c>MainViewModel</c> lets the text formatting stay headless
/// and testable.
/// </summary>
public sealed record ReportSummaryContext
{
    public string InputDirectory { get; init; } = "";

    public IReadOnlyList<ReportDetailPreview> DetailPreview { get; init; } = [];

    public IReadOnlyList<ReportDeviceSummary> HighRiskDevices { get; init; } = [];

    public IReadOnlyList<ReportIssueType> TopIssueTypes { get; init; } = [];

    public int TotalFiles { get; init; }

    public int AnomalyFiles { get; init; }

    public int AnomalyRecords { get; init; }

    public int SkippedFiles { get; init; }

    public int ReportDeviceCount { get; init; }

    public int SensorOfflineDevices { get; init; }

    public int SensorFaultRows { get; init; }

    public int SensorMissingRows { get; init; }

    public int SensorSkippedRows { get; init; }

    public string ProcessedSummary { get; init; } = "";

    public string RunElapsedText { get; init; } = "";

    public string ReportPath { get; init; } = "";
}
