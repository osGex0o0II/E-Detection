using EDetection.Desktop.Models;
using EDetection.Desktop.Services;
using EDetection.NativeCore.Models;

namespace EDetection.Tests;

public sealed class ReportDetailPreviewServiceTests
{
    [Fact]
    public void Filter_RecommendationSort_MatchesVisibleColumn()
    {
        var details = new[]
        {
            new ReportDetailPreview { Building = "A", RecommendedAction = "检查线路", Date = "2026-11-16", Time = "10:00" },
            new ReportDetailPreview { Building = "B", RecommendedAction = "复核接线", Date = "2026-11-16", Time = "09:00" },
        };

        var result = new ReportDetailPreviewService().Filter(
            details, ["全部类型"], new ReportDetailFilterState("", 0, 0, "recommendation"));

        Assert.Equal("复核接线", result[0].RecommendedAction);
        Assert.Equal("检查线路", result[1].RecommendedAction);
    }

    [Fact]
    public void Filter_TimeSort_StillOrdersLatestFirst()
    {
        var details = new[]
        {
            new ReportDetailPreview { Building = "A", Date = "2026-11-15", Time = "10:00" },
            new ReportDetailPreview { Building = "B", Date = "2026-11-16", Time = "09:00" },
        };
        var result = new ReportDetailPreviewService().Filter(
            details, ["全部类型"], new ReportDetailFilterState("", 0, 0, "time"));
        Assert.Equal("B", result[0].Building);

        var ascending = new ReportDetailPreviewService().Filter(
            details,
            ["全部类型"],
            new ReportDetailFilterState("", 0, 0, "time", SortDescending: true));
        Assert.Equal("A", ascending[0].Building);
    }

    [Fact]
    public void Filter_ValueSort_OrdersNumbersNotStrings()
    {
        var details = new[]
        {
            new ReportDetailPreview { Building = "A", IssueValue = "Uab=100.0" },
            new ReportDetailPreview { Building = "B", IssueValue = "Uab=9.0" },
            new ReportDetailPreview { Building = "C", IssueValue = "Uab=20.0" },
        };

        var result = new ReportDetailPreviewService().Filter(
            details, ["全部类型"], new ReportDetailFilterState("", 0, 0, "value"));

        Assert.Equal(["B", "C", "A"], result.Select(item => item.Building));
    }

    [Fact]
    public void IssueText_CollapsesDuplicatedTypeAndDetail()
    {
        var duplicate = new ReportDetailPreview
        {
            IssueType = "CT极性异常",
            IssueDetail = "CT极性异常",
        };
        var prefixed = new ReportDetailPreview
        {
            IssueType = "电流过大",
            IssueDetail = "电流过大: A相 1200.0A",
        };
        var combined = new ReportDetailPreview
        {
            IssueType = "电压异常",
            IssueDetail = "A相电压偏低",
        };

        Assert.Equal("CT极性异常", duplicate.IssueText);
        Assert.Equal("电流过大: A相 1200.0A", prefixed.IssueText);
        Assert.Equal("电压异常 · A相电压偏低", combined.IssueText);
    }
}
