using EDetection.NativeCore.Models;

namespace EDetection.NativeCore.Services;

public sealed partial class NativeDetectionBackendService : IDetectionBackendService
{
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
