using EDetection.NativeCore.Detection;

namespace EDetection.NativeCore.Services;

public sealed partial class NativeDetectionBackendService : IDetectionBackendService
{
    private static DetectionRuleOptions ToRuleOptions(NativeDetectionConfig config) =>
        new(
            VoltageMinThreshold: config.VoltageMinThreshold,
            VoltageMaxThreshold: config.VoltageMaxThreshold,
            VoltageImbalanceThreshold: config.VoltageImbalanceThreshold,
            CurrentMaxThreshold: config.CurrentMaxThreshold,
            CurrentUnbalanceMaxThreshold: config.CurrentUnbalanceMaxThreshold,
            CurrentActiveMinThreshold: config.CurrentActiveMinThreshold,
            ActivePowerMinThreshold: config.ActivePowerMinThreshold,
            PowerFactorMinThreshold: config.PowerFactorMinThreshold,
            TemperatureMinThreshold: config.TemperatureMinThreshold,
            TemperatureMaxThreshold: config.TemperatureMaxThreshold,
            CurrentOverloadEnabled: config.CurrentOverloadEnabled,
            CurrentUnbalanceEnabled: config.CurrentUnbalanceEnabled,
            PowerFactorEnabled: config.PowerFactorEnabled,
            DetailOutputEnabled: config.DetailOutputEnabled,
            FreezeCountThreshold: config.FreezeCountThreshold,
            FreezeStdThreshold: config.FreezeStdThreshold);
}
