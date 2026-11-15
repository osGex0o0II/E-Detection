using System.Globalization;

namespace EDetection.NativeCore.Detection;

/// <summary>
/// Ordered row-level rule pipeline. The order is part of the detection
/// contract: voltage, current, active power, power factor, temperature.
/// </summary>
public sealed class DetectionRowRulePipeline
{
    private static readonly IDetectionRowRule[] DefaultRules =
    [
        new VoltageAnomalyRule(),
        new CurrentAnomalyRule(),
        new ActivePowerAnomalyRule(),
        new PowerFactorAnomalyRule(),
        new TemperatureAnomalyRule(),
    ];

    private readonly IReadOnlyList<IDetectionRowRule> _rules;

    public DetectionRowRulePipeline(IEnumerable<IDetectionRowRule>? rules = null)
    {
        _rules = (rules ?? DefaultRules).ToArray();
    }

    public static DetectionRowRulePipeline Default { get; } = new();

    public IReadOnlyList<string> Evaluate(
        DetectionRowContext context,
        DetectionRuleOptions options)
    {
        var issueTypes = new List<string>();
        foreach (var rule in _rules)
        {
            rule.Evaluate(context, options, issueTypes);
        }

        return issueTypes;
    }
}

public sealed class VoltageAnomalyRule : IDetectionRowRule
{
    public void Evaluate(
        DetectionRowContext context,
        DetectionRuleOptions options,
        ICollection<string> issueTypes)
    {
        var values = context.VoltageValues;
        if (values.Count == 0)
        {
            return;
        }

        // A row with a missing phase is not an all-off row. The data model
        // comparison yields False for NaN, so only rows where every discovered
        // voltage column has a numeric value may suppress voltage anomalies.
        var allOff = context.VoltageColumnCount >= 2
            && values.Count == context.VoltageColumnCount
            && values.Values.All(static value => value < 1.0);
        var mean = values.Values.Average();
        var active = mean > 30.0;
        var hasPtAnomaly = false;

        if (values.Count == 3 && active && !allOff)
        {
            var median = DetectionMath.Median(values.Values);
            if (median != 0)
            {
                foreach (var (name, value) in values)
                {
                    var deviation = Math.Abs(value - median) / median;
                    var otherDeviation = values
                        .Where(pair => pair.Key != name)
                        .Select(pair => Math.Abs(pair.Value - median) / median)
                        .DefaultIfEmpty(0)
                        .Max();
                    if (deviation > 0.30 && otherDeviation < 0.15)
                    {
                        hasPtAnomaly = true;
                        if (options.DetailOutputEnabled)
                        {
                            var phase = DetectionMath.PhaseForVoltageColumn(name);
                            issueTypes.Add(
                                $"{phase}电压疑似PT接线异常(偏差{(deviation * 100.0).ToString("F1", CultureInfo.InvariantCulture)}%)");
                        }
                        else
                        {
                            issueTypes.Add("疑似PT接线异常");
                        }

                        break;
                    }
                }
            }
        }

        if (values.Count >= 2 && active && !allOff && !hasPtAnomaly && mean != 0)
        {
            var imbalancedValues = values
                .Select(pair => new
                {
                    pair.Key,
                    pair.Value,
                    Deviation = Math.Abs(pair.Value - mean) / mean,
                })
                .Where(pair => pair.Deviation > options.VoltageImbalanceThreshold)
                .ToList();
            if (options.DetailOutputEnabled)
            {
                foreach (var pair in imbalancedValues)
                {
                    var phase = DetectionMath.PhaseForVoltageColumn(pair.Key);
                    issueTypes.Add(
                        $"{phase}电压电压不平衡:{pair.Value.ToString("F1", CultureInfo.InvariantCulture)}:偏差{pair.Deviation.ToString("F3", CultureInfo.InvariantCulture)}");
                }
            }
            else if (imbalancedValues.Count > 0)
            {
                issueTypes.Add("相电压不平衡");
            }
        }

        if (!allOff)
        {
            if (options.DetailOutputEnabled)
            {
                foreach (var (phase, column) in DetectionMath.VoltagePhaseColumns())
                {
                    if (!values.TryGetValue(column, out var value))
                    {
                        continue;
                    }

                    if (value < options.VoltageMinThreshold)
                    {
                        issueTypes.Add($"{phase}电压过低");
                    }
                    else if (value > options.VoltageMaxThreshold)
                    {
                        issueTypes.Add($"{phase}电压过高");
                    }
                }
            }
            else
            {
                foreach (var value in values.Values.Where(value =>
                             value < options.VoltageMinThreshold || value > options.VoltageMaxThreshold))
                {
                    issueTypes.Add("电压异常");
                }
            }
        }
    }
}

public sealed class CurrentAnomalyRule : IDetectionRowRule
{
    public void Evaluate(
        DetectionRowContext context,
        DetectionRuleOptions options,
        ICollection<string> issueTypes)
    {
        var values = context.CurrentValues;
        if (values.Count == 0)
        {
            return;
        }

        var active = values.Values.Any(value => value >= options.CurrentActiveMinThreshold);
        if (!active)
        {
            return;
        }

        if (options.CurrentOverloadEnabled
            && values.Values.Any(value => value > options.CurrentMaxThreshold))
        {
            if (options.DetailOutputEnabled)
            {
                foreach (var phase in values
                             .Where(pair => pair.Value > options.CurrentMaxThreshold)
                             .Select(pair => $"{DetectionMath.PhaseForCurrentColumn(pair.Key)}电流过大"))
                {
                    issueTypes.Add(phase);
                }
            }
            else
            {
                // The overload rule appends its generic label once for every
                // overloaded phase even when detail_output is off. Preserve that
                // row-level multiplicity for the later structured-detail and
                // file-summary stages.
                foreach (var _ in values.Where(pair => pair.Value > options.CurrentMaxThreshold))
                {
                    issueTypes.Add("电流过大");
                }
            }
        }

        if (options.CurrentUnbalanceEnabled && values.Count >= 2)
        {
            var mean = values.Values.Average();
            if (mean != 0)
            {
                var unbalance = (values.Values.Max() - values.Values.Min()) / mean;
                if (unbalance > options.CurrentUnbalanceMaxThreshold)
                {
                    issueTypes.Add("电流不平衡");
                }
            }
        }
    }
}

public sealed class ActivePowerAnomalyRule : IDetectionRowRule
{
    public void Evaluate(
        DetectionRowContext context,
        DetectionRuleOptions options,
        ICollection<string> issueTypes)
    {
        if (!context.ActivePowerValues.TryGetValue("有功功率", out var activePower)
            || !DetectionMath.IsCurrentActive(context.CurrentValues, options))
        {
            return;
        }

        if (activePower < 0)
        {
            issueTypes.Add("CT极性异常");
            return;
        }

        if (activePower < options.ActivePowerMinThreshold)
        {
            issueTypes.Add("有功功率异常");
        }
    }
}

public sealed class PowerFactorAnomalyRule : IDetectionRowRule
{
    public void Evaluate(
        DetectionRowContext context,
        DetectionRuleOptions options,
        ICollection<string> issueTypes)
    {
        if (!options.PowerFactorEnabled
            || !context.PowerFactorValues.TryGetValue("功率因数", out var powerFactor)
            || !DetectionMath.IsCurrentActive(context.CurrentValues, options))
        {
            return;
        }

        if (powerFactor < options.PowerFactorMinThreshold)
        {
            issueTypes.Add("功率因数过低");
        }
    }
}

public sealed class TemperatureAnomalyRule : IDetectionRowRule
{
    public void Evaluate(
        DetectionRowContext context,
        DetectionRuleOptions options,
        ICollection<string> issueTypes)
    {
        var temperatureValues = context.TemperatureValues;
        if (temperatureValues.Count == 0)
        {
            return;
        }

        if (!temperatureValues.Values.Any(value =>
                value < options.TemperatureMinThreshold || value > options.TemperatureMaxThreshold))
        {
            return;
        }

        if (!options.DetailOutputEnabled)
        {
            // TemperatureRule likewise appends the generic label for each
            // out-of-range phase before the pipeline compacts it.
            foreach (var _ in temperatureValues.Where(pair =>
                         pair.Value < options.TemperatureMinThreshold
                         || pair.Value > options.TemperatureMaxThreshold))
            {
                issueTypes.Add("温度异常");
            }

            return;
        }

        foreach (var pair in temperatureValues.Where(pair =>
                     pair.Value < options.TemperatureMinThreshold
                     || pair.Value > options.TemperatureMaxThreshold))
        {
            issueTypes.Add(
                $"{pair.Key}{(pair.Value < options.TemperatureMinThreshold ? "过低" : "过高")}");
        }
    }
}
