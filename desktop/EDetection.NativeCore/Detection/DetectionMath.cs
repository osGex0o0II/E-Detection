namespace EDetection.NativeCore.Detection;

internal static class DetectionMath
{
    internal static IReadOnlyList<(string Phase, string Column)> VoltagePhaseColumns() =>
    [
        ("A相", "Uab"),
        ("B相", "Ubc"),
        ("C相", "Uca"),
    ];

    internal static string PhaseForVoltageColumn(string column) =>
        VoltagePhaseColumns()
            .FirstOrDefault(phaseColumn => phaseColumn.Column == column)
            .Phase ?? column;

    internal static string PhaseForCurrentColumn(string column) =>
        column switch
        {
            "Ia" => "A相",
            "Ib" => "B相",
            "Ic" => "C相",
            _ => column,
        };

    internal static double? GetOptionalValue(IReadOnlyDictionary<string, double> values, string key) =>
        values.TryGetValue(key, out var value) ? value : null;

    internal static bool IsCurrentActive(
        IReadOnlyDictionary<string, double> currentValues,
        DetectionRuleOptions options) =>
        currentValues.Values.Any(value => value >= options.CurrentActiveMinThreshold);

    internal static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToList();
        if (ordered.Count == 0)
        {
            return 0;
        }

        var middle = ordered.Count / 2;
        return ordered.Count % 2 == 1
            ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) / 2.0;
    }
}
