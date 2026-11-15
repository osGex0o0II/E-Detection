namespace EDetection.NativeCore.Detection;

/// <summary>
/// One parsed telemetry row. Dictionaries contain only columns that were
/// present and parseable for this row; <see cref="VoltageColumnCount"/> is the
/// number of discovered voltage columns in the file, including empty cells.
/// </summary>
public sealed record DetectionRowContext(
    IReadOnlyDictionary<string, double> VoltageValues,
    IReadOnlyDictionary<string, double> CurrentValues,
    IReadOnlyDictionary<string, double> ActivePowerValues,
    IReadOnlyDictionary<string, double> PowerFactorValues,
    IReadOnlyDictionary<string, double> TemperatureValues,
    int VoltageColumnCount);

public interface IDetectionRowRule
{
    void Evaluate(
        DetectionRowContext context,
        DetectionRuleOptions options,
        ICollection<string> issueTypes);
}
