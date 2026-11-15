using EDetection.NativeCore.Detection;

namespace EDetection.Tests;

public sealed class DetectionRulePipelineTests
{
    [Fact]
    public void VoltageAllOffSuppressesEveryVoltageIssue()
    {
        var context = Row(
            voltage: new Dictionary<string, double>
            {
                ["Uab"] = 0.4,
                ["Ubc"] = 0.5,
                ["Uca"] = 0.6,
            },
            voltageColumnCount: 3);

        var issues = DetectionRowRulePipeline.Default.Evaluate(context, new DetectionRuleOptions());

        Assert.Empty(issues);
    }

    [Fact]
    public void VoltagePtAnomalyUsesDetailedPhaseText()
    {
        var context = Row(
            voltage: new Dictionary<string, double>
            {
                ["Uab"] = 220,
                ["Ubc"] = 221,
                ["Uca"] = 400,
            },
            voltageColumnCount: 3);
        var options = new DetectionRuleOptions(DetailOutputEnabled: true);

        var issues = DetectionRowRulePipeline.Default.Evaluate(context, options);

        Assert.Contains("C相电压疑似PT接线异常(偏差81.0%)", issues);
    }

    [Fact]
    public void VoltageImbalanceIsReportedOnceWithoutDetail()
    {
        var context = Row(
            voltage: new Dictionary<string, double>
            {
                ["Uab"] = 360,
                ["Ubc"] = 400,
                ["Uca"] = 380,
            },
            voltageColumnCount: 3);

        var issues = DetectionRowRulePipeline.Default.Evaluate(context, new DetectionRuleOptions());

        Assert.Equal(["相电压不平衡"], issues);
    }

    [Fact]
    public void VoltageDetailEmitsImbalanceForEachDeviatingPhase()
    {
        var context = Row(
            voltage: new Dictionary<string, double>
            {
                ["Uab"] = 360,
                ["Ubc"] = 400,
                ["Uca"] = 380,
            },
            voltageColumnCount: 3);
        var options = new DetectionRuleOptions(DetailOutputEnabled: true);

        var issues = DetectionRowRulePipeline.Default.Evaluate(context, options);

        Assert.Equal(
            ["A相电压电压不平衡:360.0:偏差0.053", "B相电压电压不平衡:400.0:偏差0.053"],
            issues);
    }

    [Fact]
    public void VoltageDetailEmitsOneIssuePerOutOfRangePhase()
    {
        var context = Row(
            voltage: new Dictionary<string, double>
            {
                ["Uab"] = 300,
                ["Ubc"] = 300,
                ["Uca"] = 300,
            },
            voltageColumnCount: 3);
        var options = new DetectionRuleOptions(DetailOutputEnabled: true);

        var issues = DetectionRowRulePipeline.Default.Evaluate(context, options);

        Assert.Equal(
            ["A相电压过低", "B相电压过低", "C相电压过低"],
            issues);
    }

    [Fact]
    public void CurrentOverloadPreservesMultiplicityWithoutDetail()
    {
        var context = Row(
            current: new Dictionary<string, double>
            {
                ["Ia"] = 1100,
                ["Ib"] = 1200,
                ["Ic"] = 100,
            });

        var issues = DetectionRowRulePipeline.Default.Evaluate(context, new DetectionRuleOptions());

        Assert.Equal(["电流过大", "电流过大"], issues);
    }

    [Fact]
    public void CurrentUnbalanceRequiresEnabledFlag()
    {
        var context = Row(
            current: new Dictionary<string, double>
            {
                ["Ia"] = 10,
                ["Ib"] = 20,
                ["Ic"] = 30,
            });
        var options = new DetectionRuleOptions(CurrentUnbalanceEnabled: true);

        var issues = DetectionRowRulePipeline.Default.Evaluate(context, options);

        Assert.Contains("电流不平衡", issues);
    }

    [Fact]
    public void ActivePowerNegativeIsCtPolarityAnomaly()
    {
        var context = Row(
            current: new Dictionary<string, double> { ["Ia"] = 10 },
            activePower: new Dictionary<string, double> { ["有功功率"] = -5 });

        var issues = DetectionRowRulePipeline.Default.Evaluate(context, new DetectionRuleOptions());

        Assert.Contains("CT极性异常", issues);
    }

    [Fact]
    public void ActivePowerBelowConfiguredMinimumIsReported()
    {
        var context = Row(
            current: new Dictionary<string, double> { ["Ia"] = 10 },
            activePower: new Dictionary<string, double> { ["有功功率"] = 5 });
        var options = new DetectionRuleOptions(ActivePowerMinThreshold: 10);

        var issues = DetectionRowRulePipeline.Default.Evaluate(context, options);

        Assert.Contains("有功功率异常", issues);
    }

    [Fact]
    public void PowerFactorRuleRequiresEnabledFlag()
    {
        var context = Row(
            current: new Dictionary<string, double> { ["Ia"] = 10 },
            powerFactor: new Dictionary<string, double> { ["功率因数"] = 0.8 });
        var options = new DetectionRuleOptions(PowerFactorEnabled: true);

        var issues = DetectionRowRulePipeline.Default.Evaluate(context, options);

        Assert.Contains("功率因数过低", issues);
    }

    [Fact]
    public void TemperatureDetailEmitsOneIssuePerOutOfRangeSensor()
    {
        var context = Row(
            temperature: new Dictionary<string, double>
            {
                ["T1"] = 80,
                ["T2"] = -1,
            });
        var options = new DetectionRuleOptions(DetailOutputEnabled: true);

        var issues = DetectionRowRulePipeline.Default.Evaluate(context, options);

        Assert.Equal(["T1过高", "T2过低"], issues);
    }

    [Fact]
    public void TemperatureWithoutDetailPreservesOneIssuePerSensor()
    {
        var context = Row(
            temperature: new Dictionary<string, double>
            {
                ["T1"] = 80,
                ["T2"] = -1,
            });

        var issues = DetectionRowRulePipeline.Default.Evaluate(context, new DetectionRuleOptions());

        Assert.Equal(["温度异常", "温度异常"], issues);
    }

    [Fact]
    public void FreezeDetectorMarksConstantCoreRows()
    {
        var rows = Enumerable.Range(0, 3)
            .Select(_ => new FreezeDetectionRow(
                new Dictionary<string, double>
                {
                    ["Uab"] = 380,
                    ["Ubc"] = 380,
                    ["Uca"] = 380,
                    ["Ia"] = 10,
                    ["Ib"] = 10,
                    ["Ic"] = 10,
                    ["有功功率"] = 5,
                },
                new Dictionary<string, double>()))
            .ToList();

        var result = FreezeRowDetector.Detect(rows, new DetectionRuleOptions(), CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Empty(result[0]);
        Assert.Empty(result[1]);
        Assert.Contains("数据冻结", result[2]);
    }

    [Fact]
    public void FreezeDetectorSuppressesStandbyRows()
    {
        var rows = Enumerable.Range(0, 3)
            .Select(_ => new FreezeDetectionRow(
                new Dictionary<string, double>
                {
                    ["Uab"] = 380,
                    ["Ubc"] = 380,
                    ["Uca"] = 380,
                    ["Ia"] = 0,
                    ["Ib"] = 0,
                    ["Ic"] = 0,
                    ["有功功率"] = 0,
                },
                new Dictionary<string, double>()))
            .ToList();

        var result = FreezeRowDetector.Detect(rows, new DetectionRuleOptions(), CancellationToken.None);

        Assert.All(result, Assert.Empty);
    }

    [Fact]
    public void FreezeDetectorMarksConstantAuxiliaryRows()
    {
        var rows = Enumerable.Range(0, 3)
            .Select(_ => new FreezeDetectionRow(
                new Dictionary<string, double>(),
                new Dictionary<string, double> { ["无功功率"] = 5 }))
            .ToList();

        var result = FreezeRowDetector.Detect(rows, new DetectionRuleOptions(), CancellationToken.None);

        Assert.Contains("数据恒定", result[2]);
    }

    private static DetectionRowContext Row(
        IReadOnlyDictionary<string, double>? voltage = null,
        IReadOnlyDictionary<string, double>? current = null,
        IReadOnlyDictionary<string, double>? activePower = null,
        IReadOnlyDictionary<string, double>? powerFactor = null,
        IReadOnlyDictionary<string, double>? temperature = null,
        int? voltageColumnCount = null) =>
        new(
            voltage ?? new Dictionary<string, double>(),
            current ?? new Dictionary<string, double>(),
            activePower ?? new Dictionary<string, double>(),
            powerFactor ?? new Dictionary<string, double>(),
            temperature ?? new Dictionary<string, double>(),
            voltageColumnCount ?? voltage?.Count ?? 0);
}
