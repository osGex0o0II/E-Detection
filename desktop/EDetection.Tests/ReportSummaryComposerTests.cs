using EDetection.Desktop.Models;
using EDetection.Desktop.Services;
using EDetection.NativeCore.Models;

namespace EDetection.Tests;

public sealed class ReportSummaryComposerTests
{
    private readonly ReportSummaryComposer _composer = new();

    [Fact]
    public void BuildCompletionSummaryText_NoAnomalies_IncludesNormalConclusion()
    {
        var text = _composer.BuildCompletionSummaryText(new ReportSummaryContext
        {
            InputDirectory = @"D:\data\2026-11-16",
            TotalFiles = 2,
            ProcessedSummary = "2",
            AnomalyFiles = 0,
            AnomalyRecords = 0,
            ReportDeviceCount = 0,
            RunElapsedText = "3秒",
            ReportPath = "",
        });

        Assert.Contains("E-Detection 检测汇报（2026-11-16，0-23时）", text);
        Assert.Contains("简要结论：本次未见明显电气故障数据。", text);
        Assert.Contains("本时段未见明显电气异常记录，数据整体正常。", text);
        Assert.Contains("报告未生成", text);
    }

    [Fact]
    public void BuildCompletionSummaryText_OfflineIssue_DescribesDeviceAndReport()
    {
        var text = _composer.BuildCompletionSummaryText(new ReportSummaryContext
        {
            InputDirectory = @"D:\data\2026-11-16",
            DetailPreview =
            [
                new ReportDetailPreview
                {
                    Building = "A",
                    Transformer = "T1",
                    IssueType = "设备离线",
                    IssueDetail = "通信中断",
                    IssueValue = "-1.0",
                },
            ],
            HighRiskDevices =
            [
                new ReportDeviceSummary
                {
                    Building = "A",
                    Transformer = "T1",
                    MainIssueTypes = "设备离线",
                },
            ],
            TopIssueTypes =
            [
                new ReportIssueType
                {
                    Name = "设备离线",
                    Count = 1,
                },
            ],
            TotalFiles = 1,
            ProcessedSummary = "1",
            AnomalyFiles = 1,
            AnomalyRecords = 1,
            ReportDeviceCount = 1,
            RunElapsedText = "5秒",
            ReportPath = @"D:\out\report.xlsx",
        });

        Assert.Contains("E-Detection 检测汇报（2026-11-16，0-23时）", text);
        Assert.Contains("简要结论：本次主要发现A（T1）共 1 台设备通信/采集疑似中断。", text);
        Assert.Contains("A（T1）共 1 台设备全天通信/采集疑似中断", text);
        Assert.Contains("检测概况：完成 1 份文件，异常文件 1 个，异常记录 1 条，涉及设备 1 台，耗时 5秒，报告已生成：report.xlsx。", text);
    }

    [Fact]
    public void BuildCompletionSummaryText_NoAnomaliesWithSensorStatus_ReportsSensorCounts()
    {
        var text = _composer.BuildCompletionSummaryText(new ReportSummaryContext
        {
            InputDirectory = @"D:\data\2026-11-16",
            TotalFiles = 1,
            ProcessedSummary = "1",
            AnomalyRecords = 0,
            SensorOfflineDevices = 2,
            SensorFaultRows = 1,
            RunElapsedText = "1秒",
        });

        Assert.Contains("本时段未见明显电气异常记录。传感器状态存在离线记录 2 条、故障 1 条", text);
    }
}
