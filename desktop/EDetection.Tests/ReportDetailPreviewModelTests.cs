using System.ComponentModel;
using EDetection.Desktop.Models;
using EDetection.NativeCore.Models;

namespace EDetection.Tests;

public sealed class ReportDetailPreviewModelTests
{
    [Theory]
    [InlineData("2026-11-16", "7", "2026-11-16 07:00")]
    [InlineData("2026-11-16", "07", "2026-11-16 07:00")]
    [InlineData("2026-11-16", "7:30", "2026-11-16 07:30")]
    [InlineData("2026-11-16", "7:30:45", "2026-11-16 07:30")]
    [InlineData("2026-11-16", "13:05", "2026-11-16 13:05")]
    public void TimeText_NormalizesHourCellsToClockText(string date, string time, string expected)
    {
        var detail = new ReportDetailPreview { Date = date, Time = time };

        Assert.Equal(expected, detail.TimeText);
    }

    [Fact]
    public void TimeText_DropsPlaceholderDateInsteadOfPrintingIt()
    {
        // Files without a date in the name carried the literal 日期未知 and
        // every row read "日期未知 7时".
        var unknown = new ReportDetailPreview { Date = "日期未知", Time = "7" };
        var dash = new ReportDetailPreview { Date = "-", Time = "9" };

        Assert.Equal("07:00", unknown.TimeText);
        Assert.Equal("09:00", dash.TimeText);
    }

    [Fact]
    public void TimeText_FallsBackToSourceFileWhenNothingIsParsable()
    {
        var detail = new ReportDetailPreview
        {
            Date = "日期未知",
            Time = "-",
            SourceFile = "sample.csv",
        };

        Assert.Equal("sample.csv", detail.TimeText);
    }

    [Fact]
    public void TimeText_KeepsUnrecognizedTimeVerbatim()
    {
        var detail = new ReportDetailPreview { Date = "2026-11-16", Time = "上午" };

        Assert.Equal("2026-11-16 上午", detail.TimeText);
    }

    [Fact]
    public void RecentReport_IsLatest_RaisesStatusTextNotification()
    {
        var report = new RecentReport();
        var changed = new List<string?>();
        ((INotifyPropertyChanged)report).PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        report.IsLatest = true;

        // The "最新 · " prefix lives in StatusText, so a notification for
        // IsLatest alone left the label stale in the history list.
        Assert.Contains(nameof(RecentReport.StatusText), changed);
    }

    [Fact]
    public void RecentReport_IsLatest_DoesNotNotifyWhenUnchanged()
    {
        var report = new RecentReport { IsLatest = true };
        var changed = new List<string?>();
        ((INotifyPropertyChanged)report).PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        report.IsLatest = true;

        Assert.Empty(changed);
    }

    [Fact]
    public void RecentReport_StatusText_PrefixesOnlyTheLatestReport()
    {
        var latest = new RecentReport { IsLatest = true };
        var older = new RecentReport();

        Assert.StartsWith("最新 · ", latest.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("最新", older.StatusText, StringComparison.Ordinal);
    }
}
