using EDetection.Desktop.Models;
using EDetection.Desktop.Services;
using EDetection.NativeCore.Models;

namespace EDetection.Tests;

public sealed class RunStatusTextTests
{
    [Fact]
    public void IsFailure_CoversExactlyTheTwoBlockingStatuses()
    {
        Assert.True(RunStatusText.IsFailure(RunStatusText.Failed));
        Assert.True(RunStatusText.IsFailure(RunStatusText.Blocked));
    }

    [Theory]
    [InlineData(RunStatusText.Ready)]
    [InlineData(RunStatusText.Preparing)]
    [InlineData(RunStatusText.Completed)]
    [InlineData(RunStatusText.Cancelled)]
    [InlineData("")]
    [InlineData(null)]
    public void IsFailure_DoesNotFlagAnythingElse(string? statusText)
    {
        Assert.False(RunStatusText.IsFailure(statusText));
    }

    [Fact]
    public void StatusLiterals_StayDistinctSoPanelsCannotOverlap()
    {
        // The completion panel and the failure panel are driven by these two
        // constants; if they ever collapse to the same string both show at once.
        Assert.NotEqual(RunStatusText.Completed, RunStatusText.Failed);
        Assert.NotEqual(RunStatusText.Completed, RunStatusText.Blocked);
        Assert.NotEqual(RunStatusText.Cancelled, RunStatusText.Failed);
    }

    [Fact]
    public void StatusLiterals_MatchTheBackendEventMapperOutput()
    {
        // RunEventService maps backend events onto these strings; a backend
        // rename must not silently leave the UI waiting for the old text.
        var completed = new RunEventService().BuildAction(
            new DetectionBackendEvent { EventName = "run_completed" },
            new RunEventState(
                TotalFiles: 0,
                ProcessedFiles: 0,
                AnomalyFiles: 0,
                AnomalyRecords: 0,
                SkippedFiles: 0,
                ProgressPercent: 0,
                HighRiskDeviceCount: 0,
                TopIssueTypeCount: 0,
                SensorOfflineDevices: 0,
                SensorFaultRows: 0,
                SensorMissingRows: 0,
                SensorSkippedRows: 0));

        Assert.Equal(RunStatusText.Completed, completed.StatusText);
    }
}
