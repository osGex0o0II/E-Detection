using EDetection.Desktop.Models;
using EDetection.Desktop.Services;
using Microsoft.UI.Xaml;

namespace EDetection.Desktop.ViewModels;

public partial class MainViewModel
{
    // The native backend transfers at most this many detail rows to the UI and
    // keeps the rest only in the Excel report; the preview label says so.
    public const int DetailPreviewTransferLimit = 100;

    public string WorkbenchReportDeviceCountText => $"报告设备 {WorkbenchReportDeviceCount} 台";

    public Visibility CompletionActionsVisibility => ShouldShowCompletionActions
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string CompletionActionTitleText
    {
        get
        {
            if (AnomalyRecords > 0)
            {
                return $"检测完成，发现 {AnomalyRecords} 条异常";
            }

            // Zero measurement anomalies is not the same as a clean result:
            // offline sensors and skipped files are reported as faults by the
            // toast/log paths, so the headline must not claim "no anomalies".
            if (HasSensorOrSkipIssues)
            {
                return "检测完成，未发现数值异常，但存在传感器或跳过项";
            }

            return "检测完成，未发现异常";
        }
    }

    public string CompletionActionBodyText
    {
        get
        {
            var reportText = string.IsNullOrWhiteSpace(ReportPath)
                ? "未生成 Excel 报告"
                : $"报告已生成: {Path.GetFileName(ReportPath)}";
            var sensorText = HasSensorOrSkipIssues
                ? $" · 传感器问题 {SensorOfflineDevices + SensorFaultRows + SensorMissingRows} 项"
                : "";
            return $"{ProcessedSummary} 个文件 · 异常文件 {AnomalyFiles} · 跳过 {SkippedFiles}{sensorText} · {reportText}";
        }
    }

    private bool HasSensorOrSkipIssues =>
        SensorOfflineDevices > 0
        || SensorFaultRows > 0
        || SensorMissingRows > 0
        || SkippedFiles > 0;

    private bool ShouldShowCompletionActions =>
        !IsRunning && SelectedReport is null && StatusText == RunStatusText.Completed;

    // Only a bounded window of detail rows is transferred to the UI (the
    // backend keeps the first 100).  Showing "100/600 条预览" reads like a
    // filter result rather than a truncation, so call the limit out explicitly.
    private static string BuildPreviewStatusText(int visibleCount, int totalCount) =>
        totalCount > DetailPreviewTransferLimit
            ? $"显示前 {visibleCount} 条 · 共 {totalCount} 条"
            : $"{visibleCount}/{totalCount} 条预览";

    public string DetailSortStatusText => SelectedDetailSortKey switch
    {
        "severity" => DetailSortPresentation.SortStatusText("等级", SelectedDetailSortKey, "severity", DetailSortDescending),
        "device" => DetailSortPresentation.SortStatusText("设备", SelectedDetailSortKey, "device", DetailSortDescending),
        "time" => DetailSortPresentation.SortStatusText("时间", SelectedDetailSortKey, "time", DetailSortDescending),
        "issue" => DetailSortPresentation.SortStatusText("异常", SelectedDetailSortKey, "issue", DetailSortDescending),
        "value" => DetailSortPresentation.SortStatusText("异常值", SelectedDetailSortKey, "value", DetailSortDescending),
        "recommendation" => DetailSortPresentation.SortStatusText("建议", SelectedDetailSortKey, "recommendation", DetailSortDescending),
        _ => "默认顺序",
    };

    // Sighted users need the same state the screen reader gets from
    // DetailSortStatusText, so the active column carries an arrow.
    public string DetailSeverityHeaderText =>
        DetailSortPresentation.HeaderText("等级", SelectedDetailSortKey, "severity", DetailSortDescending);

    public string DetailDeviceHeaderText =>
        DetailSortPresentation.HeaderText("设备", SelectedDetailSortKey, "device", DetailSortDescending);

    public string DetailIssueHeaderText =>
        DetailSortPresentation.HeaderText("异常", SelectedDetailSortKey, "issue", DetailSortDescending);

    public string DetailValueHeaderText =>
        DetailSortPresentation.HeaderText("异常值", SelectedDetailSortKey, "value", DetailSortDescending);

    public string DetailRecommendationHeaderText =>
        DetailSortPresentation.HeaderText("建议", SelectedDetailSortKey, "recommendation", DetailSortDescending);

    public string DetailTableEmptyStateTitle => WorkbenchDetailPreviewTotalCount == 0
        ? "暂无异常明细"
        : "没有匹配的异常明细";

    public string DetailTableEmptyStateHint => WorkbenchDetailPreviewTotalCount == 0
        ? "完成一次检测或选择历史报告后，可在这里查看异常记录。"
        : "调整搜索关键词或筛选条件后再试。";

    // With detail output disabled every row carries "-" in the value column.
    public Visibility DetailValueColumnVisibility => DetailOutputEnabled
        ? Visibility.Visible
        : Visibility.Collapsed;

    public GridLength DetailValueColumnWidth => DetailOutputEnabled
        ? new GridLength(86)
        : new GridLength(0);

    public Visibility DetailTableEmptyStateVisibility =>
        WorkbenchDetailPreviewTotalCount == 0 || !WorkbenchFilteredDetailPreview.Any()
            ? Visibility.Visible
            : Visibility.Collapsed;
}
