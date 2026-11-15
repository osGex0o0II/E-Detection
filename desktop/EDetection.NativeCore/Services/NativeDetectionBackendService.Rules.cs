using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using EDetection.NativeCore.Models;

namespace EDetection.NativeCore.Services;

public sealed partial class NativeDetectionBackendService : IDetectionBackendService
{
    private static IReadOnlyList<string> DetectVoltageAnomalyTypes(
        Dictionary<string, double> values,
        int voltageColumnCount,
        NativeDetectionConfig config)
    {
        if (values.Count == 0)
        {
            return [];
        }

        var types = new List<string>();
        // A row with a missing phase is not an all-off row. The data model
        // comparison yields False for NaN, so only rows where every discovered
        // voltage column has a numeric value may suppress voltage anomalies.
        var allOff = voltageColumnCount >= 2
            && values.Count == voltageColumnCount
            && values.Values.All(static value => value < 1.0);
        var mean = values.Values.Average();
        var active = mean > 30.0;
        var hasPtAnomaly = false;

        if (values.Count == 3 && active && !allOff)
        {
            var median = Median(values.Values);
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
                        if (config.DetailOutputEnabled)
                        {
                            var phase = PhaseForVoltageColumn(name);
                            types.Add(
                                $"{phase}电压疑似PT接线异常(偏差{(deviation * 100.0).ToString("F1", CultureInfo.InvariantCulture)}%)");
                        }
                        else
                        {
                            types.Add("疑似PT接线异常");
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
                .Where(pair => pair.Deviation > config.VoltageImbalanceThreshold)
                .ToList();
            if (config.DetailOutputEnabled)
            {
                foreach (var pair in imbalancedValues)
                {
                    var phase = PhaseForVoltageColumn(pair.Key);
                    types.Add(
                        $"{phase}电压电压不平衡:{pair.Value.ToString("F1", CultureInfo.InvariantCulture)}:偏差{pair.Deviation.ToString("F3", CultureInfo.InvariantCulture)}");
                }
            }
            else if (imbalancedValues.Count > 0)
            {
                types.Add("相电压不平衡");
            }
        }

        if (!allOff)
        {
            if (config.DetailOutputEnabled)
            {
                foreach (var (phase, column) in VoltagePhaseColumns())
                {
                    if (!values.TryGetValue(column, out var value))
                    {
                        continue;
                    }

                    if (value < config.VoltageMinThreshold)
                    {
                        types.Add($"{phase}电压过低");
                    }
                    else if (value > config.VoltageMaxThreshold)
                    {
                        types.Add($"{phase}电压过高");
                    }
                }
            }
            else
            {
                foreach (var value in values.Values.Where(value => value < config.VoltageMinThreshold || value > config.VoltageMaxThreshold))
                {
                    types.Add("电压异常");
                }
            }
        }

        return types;
    }

    private static IReadOnlyList<string> DetectCurrentAnomalyTypes(
        Dictionary<string, double> values,
        NativeDetectionConfig config)
    {
        if (values.Count == 0)
        {
            return [];
        }

        var types = new List<string>();
        var active = values.Values.Any(value => value >= config.CurrentActiveMinThreshold);
        if (!active)
        {
            return types;
        }

        if (config.CurrentOverloadEnabled
            && values.Values.Any(value => value > config.CurrentMaxThreshold))
        {
            if (config.DetailOutputEnabled)
            {
                types.AddRange(
                    values
                        .Where(pair => pair.Value > config.CurrentMaxThreshold)
                        .Select(pair => $"{PhaseForCurrentColumn(pair.Key)}电流过大"));
            }
            else
            {
                // The overload rule appends its generic label once
                // for every overloaded phase even when detail_output is off.
                // Preserve that row-level multiplicity for the later
                // structured-detail and file-summary stages.
                types.AddRange(values
                    .Where(pair => pair.Value > config.CurrentMaxThreshold)
                    .Select(static _ => "电流过大"));
            }
        }

        if (config.CurrentUnbalanceEnabled && values.Count >= 2)
        {
            var mean = values.Values.Average();
            if (mean != 0)
            {
                var unbalance = (values.Values.Max() - values.Values.Min()) / mean;
                if (unbalance > config.CurrentUnbalanceMaxThreshold)
                {
                    types.Add("电流不平衡");
                }
            }
        }

        return types;
    }

    private static IReadOnlyList<string> DetectActivePowerAnomalyTypes(
        Dictionary<string, double> activePowerValues,
        Dictionary<string, double> currentValues,
        NativeDetectionConfig config)
    {
        if (!activePowerValues.TryGetValue("有功功率", out var activePower)
            || !IsCurrentActive(currentValues, config))
        {
            return [];
        }

        if (activePower < 0)
        {
            return ["CT极性异常"];
        }

        return activePower < config.ActivePowerMinThreshold
            ? ["有功功率异常"]
            : [];
    }

    private static IReadOnlyList<string> DetectPowerFactorAnomalyTypes(
        Dictionary<string, double> powerFactorValues,
        Dictionary<string, double> currentValues,
        NativeDetectionConfig config)
    {
        if (!config.PowerFactorEnabled
            || !powerFactorValues.TryGetValue("功率因数", out var powerFactor)
            || !IsCurrentActive(currentValues, config))
        {
            return [];
        }

        return powerFactor < config.PowerFactorMinThreshold
            ? ["功率因数过低"]
            : [];
    }

    private static bool IsCurrentActive(Dictionary<string, double> currentValues, NativeDetectionConfig config) =>
        currentValues.Values.Any(value => value >= config.CurrentActiveMinThreshold);

    private static IReadOnlyList<string> DetectTemperatureAnomalyTypes(
        Dictionary<string, double> temperatureValues,
        NativeDetectionConfig config)
    {
        if (temperatureValues.Count == 0)
        {
            return [];
        }

        if (!temperatureValues.Values.Any(value =>
                value < config.TemperatureMinThreshold || value > config.TemperatureMaxThreshold))
        {
            return [];
        }

        if (!config.DetailOutputEnabled)
        {
            // TemperatureRule likewise appends the generic label for each
            // out-of-range phase before the pipeline compacts it.
            return temperatureValues
                .Where(pair => pair.Value < config.TemperatureMinThreshold
                               || pair.Value > config.TemperatureMaxThreshold)
                .Select(static _ => "温度异常")
                .ToList();
        }

        return temperatureValues
            .Where(pair => pair.Value < config.TemperatureMinThreshold
                           || pair.Value > config.TemperatureMaxThreshold)
            .Select(pair =>
                $"{pair.Key}{(pair.Value < config.TemperatureMinThreshold ? "过低" : "过高")}")
            .ToList();
    }

    private static IReadOnlyList<IReadOnlyList<string>> DetectFreezeRowTypes(
        IReadOnlyList<NativeRowAnalysis> rows,
        NativeDetectionConfig config,
        CancellationToken cancellationToken)
    {
        var result = rows
            .Select(static _ => new List<string>())
            .ToList();
        if (rows.Count == 0)
        {
            return result;
        }

        foreach (var column in new[] { "无功功率", "功率因数" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = rows.Select(row => GetOptionalValue(row.FreezeAuxValues, column)).ToList();
            if (values.All(static value => Math.Abs(value ?? 0) < 0.000_001))
            {
                continue;
            }

            var frozen = DetectFlatRun(
                values,
                config.FreezeCountThreshold,
                config.FreezeStdThreshold,
                cancellationToken);
            for (var index = 0; index < frozen.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (frozen[index])
                {
                    result[index].Add("数据恒定");
                }
            }
        }

        var coreMasks = new List<IReadOnlyList<bool>>();
        foreach (var column in new[] { "Uab", "Ubc", "Uca", "Ia", "Ib", "Ic", "有功功率" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = rows.Select(row => GetOptionalValue(row.FreezeCoreValues, column)).ToList();
            if (values.All(static value => Math.Abs(value ?? 0) < 0.000_001))
            {
                continue;
            }

            coreMasks.Add(DetectFlatRun(
                values,
                config.FreezeCountThreshold,
                config.FreezeStdThreshold,
                cancellationToken));
        }

        if (coreMasks.Count == 0)
        {
            return result;
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frozenCount = coreMasks.Count(mask => mask[rowIndex]);
            if (frozenCount >= 2 && !IsStandbyRow(rows[rowIndex], config))
            {
                result[rowIndex].Add("数据冻结");
            }
        }

        return result;
    }

    private static IReadOnlyList<bool> DetectFlatRun(
        IReadOnlyList<double?> values,
        int countNeed,
        double stdLimit,
        CancellationToken cancellationToken)
    {
        var flat = new bool[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index == 0)
            {
                flat[index] = true;
                continue;
            }

            // pandas evaluates a difference involving NaN as NaN, then
            // FreezeRule applies fillna(0) before the threshold comparison.
            // Therefore a missing current or previous sample is flat, rather
            // than being compared against a synthetic numeric zero.
            if (!values[index].HasValue || !values[index - 1].HasValue)
            {
                flat[index] = true;
                continue;
            }

            flat[index] = Math.Abs(values[index]!.Value - values[index - 1]!.Value) < stdLimit;
        }

        var frozen = new bool[values.Count];
        if (countNeed <= 0)
        {
            countNeed = 1;
        }

        var flatCount = 0;
        for (var index = 0; index < flat.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (flat[index])
            {
                flatCount++;
            }

            if (index >= countNeed && flat[index - countNeed])
            {
                flatCount--;
            }

            frozen[index] = index + 1 >= countNeed && flatCount >= countNeed;
        }

        return frozen;
    }

    private static bool IsStandbyRow(NativeRowAnalysis row, NativeDetectionConfig config)
    {
        var currentColumns = new[] { "Ia", "Ib", "Ic" }
            .Where(column => row.FreezeCoreValues.ContainsKey(column))
            .ToList();
        if (currentColumns.Count == 0)
        {
            return false;
        }

        var allCurrentLow = currentColumns.All(column =>
            Math.Abs(GetOptionalValue(row.FreezeCoreValues, column) ?? 0) < config.CurrentActiveMinThreshold);
        if (!allCurrentLow)
        {
            return false;
        }

        var voltageColumns = new[] { "Uab", "Ubc", "Uca" }
            .Where(column => row.FreezeCoreValues.ContainsKey(column))
            .ToList();
        return voltageColumns.Count == 0
            || voltageColumns.All(column =>
                GetOptionalValue(row.FreezeCoreValues, column) is { } value
                && value >= config.VoltageMinThreshold
                && value <= config.VoltageMaxThreshold);
    }

    private static double? GetOptionalValue(IReadOnlyDictionary<string, double> values, string key) =>
        values.TryGetValue(key, out var value) ? value : null;

    private static bool IsFreezeOnlyType(string type) =>
        type.Contains("数据冻结", StringComparison.Ordinal)
        || type.Contains("数据恒定", StringComparison.Ordinal)
        || type.Contains("传感器缺失", StringComparison.Ordinal)
        || type.Contains("恒定", StringComparison.Ordinal)
        || type.Contains("缺失", StringComparison.Ordinal);

    private static void UpdateOfflineInvalidCounts(
        IReadOnlyList<string> cells,
        IReadOnlyList<NativeColumnGroup> columnGroups,
        Dictionary<string, int> counts)
    {
        foreach (var group in columnGroups)
        {
            var values = group.Indexes
                .Where(index => index < cells.Count)
                .Select(index => TryParseDouble(cells[index], out var parsed) ? (double?)parsed : null)
                .Where(static value => value.HasValue)
                .Select(static value => value!.Value)
                .ToList();
            if ((values.Count == 0 || IsInvalidValue(values.Average()))
                && counts.ContainsKey(group.Name))
            {
                counts[group.Name]++;
            }
        }
    }

    private static void UpdateSensorStates(
        IReadOnlyList<string> cells,
        IReadOnlyList<NativeColumnGroup> columnGroups,
        Dictionary<string, NativeSensorColumnState> states)
    {
        foreach (var group in columnGroups)
        {
            if (!states.TryGetValue(group.Name, out var state))
            {
                continue;
            }

            // A present header with a blank/missing cell is still an observed
            // sensor value; pandas later fills that NaN with zero for the
            // all-zero (missing sensor) check.
            state.HasObservedCell = true;
            var numericValues = group.Indexes
                .Where(index => index < cells.Count)
                .Select(index => TryParseDouble(cells[index], out var parsed) ? (double?)parsed : null)
                .Where(static value => value.HasValue)
                .Select(static value => value!.Value)
                .ToList();
            if (numericValues.Count == 0)
            {
                continue;
            }

            // Merge duplicate aliases by averaging them
            // before invalid-value statistics and rule evaluation.
            var value = numericValues.Average();
            if (IsTemperatureColumn(group.Name) && Math.Abs(value - 2867.2) < 0.000_001)
            {
                state.FaultCount++;
            }

            if (!IsInvalidValue(value) && Math.Abs(value) > 0.000_001)
            {
                state.HasNonZeroValidValue = true;
            }
        }
    }

}
