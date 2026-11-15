namespace EDetection.NativeCore.Detection;

/// <summary>
/// The subset of a parsed row needed by the freeze detector. Keeping this
/// separate from the backend's private row-analysis record lets the freeze
/// logic be tested without exposing service internals.
/// </summary>
public sealed record FreezeDetectionRow(
    IReadOnlyDictionary<string, double> CoreValues,
    IReadOnlyDictionary<string, double> AuxValues);

public static class FreezeRowDetector
{
    public static IReadOnlyList<IReadOnlyList<string>> Detect(
        IReadOnlyList<FreezeDetectionRow> rows,
        DetectionRuleOptions options,
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
            var values = rows.Select(row => DetectionMath.GetOptionalValue(row.AuxValues, column)).ToList();
            if (values.All(static value => Math.Abs(value ?? 0) < 0.000_001))
            {
                continue;
            }

            var frozen = DetectFlatRun(
                values,
                options.FreezeCountThreshold,
                options.FreezeStdThreshold,
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
            var values = rows.Select(row => DetectionMath.GetOptionalValue(row.CoreValues, column)).ToList();
            if (values.All(static value => Math.Abs(value ?? 0) < 0.000_001))
            {
                continue;
            }

            coreMasks.Add(DetectFlatRun(
                values,
                options.FreezeCountThreshold,
                options.FreezeStdThreshold,
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
            if (frozenCount >= 2 && !IsStandbyRow(rows[rowIndex], options))
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

    private static bool IsStandbyRow(FreezeDetectionRow row, DetectionRuleOptions options)
    {
        var currentColumns = new[] { "Ia", "Ib", "Ic" }
            .Where(column => row.CoreValues.ContainsKey(column))
            .ToList();
        if (currentColumns.Count == 0)
        {
            return false;
        }

        var allCurrentLow = currentColumns.All(column =>
            Math.Abs(DetectionMath.GetOptionalValue(row.CoreValues, column) ?? 0)
            < options.CurrentActiveMinThreshold);
        if (!allCurrentLow)
        {
            return false;
        }

        var voltageColumns = new[] { "Uab", "Ubc", "Uca" }
            .Where(column => row.CoreValues.ContainsKey(column))
            .ToList();
        return voltageColumns.Count == 0
            || voltageColumns.All(column =>
                DetectionMath.GetOptionalValue(row.CoreValues, column) is { } value
                && value >= options.VoltageMinThreshold
                && value <= options.VoltageMaxThreshold);
    }
}
