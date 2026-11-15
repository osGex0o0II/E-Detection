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
    private sealed record NativeDetectionConfig(
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

    private sealed record NativeConfigLoadResult(
        NativeDetectionConfig Config,
        bool IsSuccess,
        string? ErrorType = null,
        string? ErrorMessage = null)
    {
        public static NativeConfigLoadResult Success(NativeDetectionConfig config) =>
            new(config, true);

        public static NativeConfigLoadResult Error(string errorType, string message) =>
            new(new NativeDetectionConfig(), false, errorType, message);
    }

    private sealed record NativeColumnGroup(
        string Name,
        IReadOnlyList<int> Indexes);

    private sealed record NativeLocation(
        string Building,
        string Transformer);

    private sealed record NativeRowAnalysis(
        string Time,
        IReadOnlyList<string> AnomalyTypes,
        IReadOnlyDictionary<string, double> FreezeCoreValues,
        IReadOnlyDictionary<string, double> FreezeAuxValues,
        IReadOnlyDictionary<string, double> ReportValues);

    private sealed record NativeFileAnalysis(
        string Status,
        string Message,
        int AnomalyCount = 0,
        string AnomalyTypes = "",
        NativeSensorStatusRow? SensorStatus = null,
        IReadOnlyList<ReportDetailPreview>? DetailPreview = null,
        NativeParityTrace? ParityTrace = null)
    {
        public NativeSensorStatusRow SensorStatus { get; } = SensorStatus ?? NativeSensorStatusRow.Empty;

        public IReadOnlyList<ReportDetailPreview> DetailPreview { get; } = DetailPreview ?? [];

        public static NativeFileAnalysis Normal(
            string fileName,
            IReadOnlyList<string>? sensorMissing = null,
            NativeSensorStatusRow? sensorStatus = null,
            NativeParityTrace? parityTrace = null) =>
            new(
                "normal",
                $"正常 {fileName}{BuildMissingSuffix(sensorMissing)}",
                SensorStatus: sensorStatus,
                ParityTrace: parityTrace);

        public static NativeFileAnalysis Anomaly(
            string fileName,
            int anomalyCount,
            string anomalyTypes,
            IReadOnlyList<string>? sensorMissing = null,
            NativeSensorStatusRow? sensorStatus = null,
            IReadOnlyList<ReportDetailPreview>? detailPreview = null,
            NativeParityTrace? parityTrace = null) =>
            new(
                "anomaly",
                $"异常 {fileName}: {anomalyCount} 条{BuildMissingSuffix(sensorMissing)} [{anomalyTypes}]",
                anomalyCount,
                anomalyTypes,
                sensorStatus,
                detailPreview,
                parityTrace);

        public static NativeFileAnalysis Skipped(string message) =>
            new("skipped", message);

        private static string BuildMissingSuffix(IReadOnlyList<string>? sensorMissing) =>
            sensorMissing is { Count: > 0 }
                ? $" [未配置: {string.Join(", ", sensorMissing)}]"
                : "";
    }

    private sealed record NativeSensorStatusRow(
        string SourceFile,
        string Building,
        string RelativePath,
        string Transformer,
        bool IsOffline,
        IReadOnlyList<string> SensorFaults,
        IReadOnlyList<string> SensorMissing,
        string Status,
        string Reason)
    {
        public static NativeSensorStatusRow Empty { get; } = new("", "", "", "", false, [], [], "", "");

        public static NativeSensorStatusRow Skipped(
            string fileName,
            string relativePath,
            NativeLocation location,
            string reason) =>
            new(fileName, location.Building, relativePath, location.Transformer, false, [], [], "跳过", reason);
    }

    private sealed record NativeSensorFault(
        string Name,
        int Count);

    private sealed class NativeSensorColumnState
    {
        public bool HasObservedCell { get; set; }

        public bool HasNonZeroValidValue { get; set; }

        public int FaultCount { get; set; }
    }
}
