using Microsoft.UI.Xaml;

namespace EDetection.Desktop.ViewModels;

public partial class MainViewModel
{
    public string WorkbenchReportDeviceCountText => $"报告设备 {WorkbenchReportDeviceCount} 台";

    public string DetailSortStatusText => SelectedDetailSortKey switch
    {
        "severity" => SortStatusText("等级"),
        "device" => SortStatusText("设备"),
        "time" => SortStatusText("时间"),
        "issue" => SortStatusText("异常"),
        "value" => SortStatusText("异常值"),
        "recommendation" => SortStatusText("建议"),
        _ => "默认顺序",
    };

    public string DetailTableEmptyStateTitle => WorkbenchDetailPreviewTotalCount == 0
        ? "暂无异常明细"
        : "没有匹配的异常明细";

    public string DetailTableEmptyStateHint => WorkbenchDetailPreviewTotalCount == 0
        ? "完成一次检测或选择历史报告后，可在这里查看异常记录。"
        : "调整搜索关键词或筛选条件后再试。";

    public Visibility DetailTableEmptyStateVisibility =>
        WorkbenchDetailPreviewTotalCount == 0 || !WorkbenchFilteredDetailPreview.Any()
            ? Visibility.Visible
            : Visibility.Collapsed;

    private string SortStatusText(string label)
    {
        // Time's natural order is newest-first; every other column is
        // ascending until the direction flag is set.
        var descending = SelectedDetailSortKey == "time"
            ? !DetailSortDescending
            : DetailSortDescending;
        return $"按{label}{(descending ? "降序" : "升序")}";
    }
}
