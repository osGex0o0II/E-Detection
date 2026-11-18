using System.Collections.ObjectModel;
using EDetection.Desktop.Models;
using EDetection.Desktop.Services;

namespace EDetection.Tests;

public sealed class RetentionTrimTests
{
    [Fact]
    public void ReportHistoryTrim_RemovesOldestEntriesDownToTheLimit()
    {
        // Lowering "报告历史上限" deletes history immediately, so the trim count
        // is what the settings card reports back to the user.
        var reports = new ObservableCollection<RecentReport>(
            Enumerable.Range(0, 5).Select(i => new RecentReport { Path = $"r{i}.xlsx" }));

        new ReportHistoryService().Trim(reports, 2);

        Assert.Equal(2, reports.Count);
        Assert.Equal(["r0.xlsx", "r1.xlsx"], reports.Select(report => report.Path));
    }

    [Fact]
    public void ReportHistoryTrim_IsNoOpWhenAlreadyWithinTheLimit()
    {
        var reports = new ObservableCollection<RecentReport>(
            Enumerable.Range(0, 2).Select(i => new RecentReport { Path = $"r{i}.xlsx" }));

        new ReportHistoryService().Trim(reports, 50);

        Assert.Equal(2, reports.Count);
    }

    [Fact]
    public void RuntimeLogTrim_RemovesOldestEntriesDownToTheLimit()
    {
        var logs = new ObservableCollection<DetectionLogItem>();
        var service = new RuntimeLogService();
        for (var i = 0; i < 5; i++)
        {
            service.Add(logs, "文件", $"消息 {i}", retentionLimit: 100);
        }

        service.Trim(logs, 2);

        Assert.Equal(2, logs.Count);
    }

    [Fact]
    public void RuntimeLogAdd_KeepsNewestFirstAndHonoursTheLimit()
    {
        var logs = new ObservableCollection<DetectionLogItem>();
        var service = new RuntimeLogService();

        for (var i = 0; i < 4; i++)
        {
            service.Add(logs, "文件", $"消息 {i}", retentionLimit: 2);
        }

        Assert.Equal(2, logs.Count);
        Assert.Equal("消息 3", logs[0].Message);
        Assert.Equal("消息 2", logs[1].Message);
    }

    [Fact]
    public void RuntimeLogExport_UsesLocalizedKindAndRedactsLocalPaths()
    {
        var logs = new ObservableCollection<DetectionLogItem>();
        var service = new RuntimeLogService();
        service.Add(
            logs,
            "文件",
            @"无法读取 C:\Users\someone\Desktop\secret\sample.csv",
            retentionLimit: 10);

        var text = service.BuildExportText(logs);

        Assert.Contains("文件", text, StringComparison.Ordinal);
        // The export must not leak machine-specific paths to a copied buffer
        // or a support bundle.
        Assert.DoesNotContain(@"C:\Users\someone", text, StringComparison.Ordinal);
    }
}
