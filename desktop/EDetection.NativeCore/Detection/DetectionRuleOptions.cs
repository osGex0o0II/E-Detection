namespace EDetection.NativeCore.Detection;

/// <summary>
/// Thresholds and switches used by the row-level detection rules.
/// Mirrors <c>NativeDetectionConfig</c> so the rule pipeline can be tested
/// without constructing the backend service.
/// </summary>
public sealed record DetectionRuleOptions(
    double VoltageMinThreshold = 353.0,
    double VoltageMaxThreshold = 430.0,
    double VoltageImbalanceThreshold = 0.02,
    double CurrentMaxThreshold = 1000.0,
    double CurrentUnbalanceMaxThreshold = 0.15,
    double CurrentActiveMinThreshold = 1.0,
    double ActivePowerMinThreshold = 0.0,
    double PowerFactorMinThreshold = 0.90,
    double TemperatureMinThreshold = 0.0,
    double TemperatureMaxThreshold = 70.0,
    bool CurrentOverloadEnabled = true,
    bool CurrentUnbalanceEnabled = false,
    bool PowerFactorEnabled = false,
    bool DetailOutputEnabled = false,
    int FreezeCountThreshold = 3,
    double FreezeStdThreshold = 0.01);
