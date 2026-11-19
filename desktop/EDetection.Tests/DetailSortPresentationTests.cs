using EDetection.Desktop.Services;
using EDetection.NativeCore.Models;

namespace EDetection.Tests;

public sealed class DetailSortPresentationTests
{
    [Fact]
    public void HeaderText_LeavesInactiveColumnsPlain()
    {
        Assert.Equal("等级", DetailSortPresentation.HeaderText("等级", "device", "severity", descending: true));
    }

    [Theory]
    [InlineData(false, "等级 ↑")]
    [InlineData(true, "等级 ↓")]
    public void HeaderText_AppendsTheDirectionArrow(bool descending, string expected)
    {
        Assert.Equal(expected, DetailSortPresentation.HeaderText("等级", "severity", "severity", descending));
    }

    [Theory]
    [InlineData(false, "时间 ↓")]
    [InlineData(true, "时间 ↑")]
    public void HeaderText_InvertsTheArrowForTimeBecauseNewestComesFirst(bool descending, string expected)
    {
        // Time's natural order is newest-first, so an unset flag already means
        // descending and the arrow must follow the visible order.
        Assert.Equal(expected, DetailSortPresentation.HeaderText("时间", "time", "time", descending));
    }

    [Theory]
    [InlineData("severity", false, "按等级升序")]
    [InlineData("severity", true, "按等级降序")]
    [InlineData("time", false, "按时间降序")]
    [InlineData("time", true, "按时间升序")]
    public void SortStatusText_MatchesTheVisibleOrder(string key, bool descending, string expected)
    {
        var label = key == "time" ? "时间" : "等级";
        Assert.Equal(expected, DetailSortPresentation.SortStatusText(label, key, key, descending));
    }

    [Fact]
    public void MostSevere_PrefersHighSeverityThenTheEarliestTime()
    {
        var details = new[]
        {
            new ReportDetailPreview { Severity = "低", Time = "2026-11-16", Building = "低" },
            new ReportDetailPreview { Severity = "高", Time = "2026-11-16", Building = "高-晚", Transformer = "B" },
            new ReportDetailPreview { Severity = "高", Time = "2026-11-15", Building = "高-早" },
            new ReportDetailPreview { Severity = "中", Time = "2026-11-16", Building = "中" },
        };

        var worst = DetailSortPresentation.MostSevere(details);

        Assert.NotNull(worst);
        Assert.Equal("高-早", worst.Building);
    }

    [Fact]
    public void MostSevere_ReturnsNullForAnEmptySelection()
    {
        Assert.Null(DetailSortPresentation.MostSevere([]));
    }

    [Fact]
    public void SeverityRank_OrdersHighBeforeLowAndUnknownLast()
    {
        Assert.True(DetailSortPresentation.SeverityRank("高") < DetailSortPresentation.SeverityRank("中"));
        Assert.True(DetailSortPresentation.SeverityRank("中") < DetailSortPresentation.SeverityRank("低"));
        Assert.True(DetailSortPresentation.SeverityRank("低") < DetailSortPresentation.SeverityRank(null));
    }
}
