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
    private static ReportDetailPreview BuildDetailPreview(
        string fileName,
        string relativePath,
        NativeLocation location,
        string issueType,
        string issueDetail,
        string severity,
        string issueValue,
        string? time = null,
        IReadOnlyDictionary<string, double>? reportValues = null) =>
        new()
        {
            Building = location.Building,
            Transformer = location.Transformer,
            RelativePath = relativePath,
            SourceFile = fileName,
            Date = ExtractDateFromFileName(fileName),
            Time = time,
            Severity = severity,
            IssueType = issueType,
            IssueDetail = issueDetail,
            IssueValue = issueValue,
            RecommendedAction = RecommendedActionForIssueType(issueType),
            ReportValues = reportValues is null
                ? []
                : reportValues.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
        };

    private static string SeverityForIssueType(string issueType)
    {
        if (issueType.Contains("设备离线", StringComparison.Ordinal)
            || issueType.Contains("PT接线异常", StringComparison.Ordinal)
            || issueType.Contains("CT", StringComparison.Ordinal)
            || issueType.Contains("CT极性异常", StringComparison.Ordinal))
        {
            return "高";
        }

        return IsFreezeOnlyType(issueType) ? "低" : "中";
    }

    private static string RecommendedActionForIssueType(string issueType)
    {
        if (issueType.Contains("PT接线异常", StringComparison.Ordinal))
        {
            return "优先检查 PT 二次接线、端子松动和采样通道配置。";
        }

        if (issueType.Contains("CT极性异常", StringComparison.Ordinal))
        {
            return "检查 CT 二次侧极性、功率方向和接线相序。";
        }

        if (issueType.Contains("设备离线", StringComparison.Ordinal))
        {
            return "核查设备通信、电源状态和采集网关在线状态。";
        }

        if (issueType.Contains("电压", StringComparison.Ordinal))
        {
            return "复核电压采样、负载状态和对应回路运行方式。";
        }

        if (issueType.Contains("电流", StringComparison.Ordinal))
        {
            return "检查负载分配、三相平衡和额定容量匹配情况。";
        }

        if (issueType.Contains("功率因数", StringComparison.Ordinal))
        {
            return "检查无功补偿、负载性质和功率因数采样配置。";
        }

        if (issueType.Contains("温度", StringComparison.Ordinal))
        {
            return "检查柜内散热、负载水平和温度传感器安装状态。";
        }

        if (issueType.Contains("数据冻结", StringComparison.Ordinal)
            || issueType.Contains("数据恒定", StringComparison.Ordinal))
        {
            return "优先排查采集链路、网关缓存和测点刷新周期。";
        }

        if (issueType.Contains("传感器", StringComparison.Ordinal))
        {
            return "核查传感器配置、接线和量程映射。";
        }

        return "结合现场运行方式复核该时段数据。";
    }

    private static string BuildSensorStatusReason(
        bool isOffline,
        IReadOnlyList<NativeSensorFault> sensorFaults,
        IReadOnlyList<string> sensorMissing)
    {
        var reasons = new List<string>();
        if (isOffline)
        {
            reasons.Add("关键电气参数离线值超过50%");
        }

        if (sensorFaults.Count > 0)
        {
            reasons.Add($"传感器故障: {string.Join("；", sensorFaults.Select(static fault => $"{fault.Name}({fault.Count})"))}");
        }

        if (sensorMissing.Count > 0)
        {
            reasons.Add($"传感器未配置: {string.Join("；", sensorMissing)}");
        }

        return string.Join("；", reasons);
    }

    private static string BuildSensorFaultSummaryType(NativeSensorFault fault) =>
        $"传感器故障({fault.Name})";

    private static string BuildSensorFaultIssueType(NativeSensorFault fault) =>
        $"传感器故障({fault.Name}:2867.2°C,共{fault.Count}点)";

    private static string ExtractDateFromFileName(string fileName)
    {
        var matches = DatePattern().Matches(fileName);
        if (matches.Count == 0)
        {
            return "日期未知";
        }

        var candidate = matches[^1].Value
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("/", "", StringComparison.Ordinal);
        if (candidate.Length != 8)
        {
            return "日期未知";
        }

        return $"{candidate[..4]}-{candidate.Substring(4, 2)}-{candidate.Substring(6, 2)}";
    }

    private static NativeLocation ExtractLocation(string path, string inputRoot)
    {
        var relativePath = Path.GetRelativePath(inputRoot, path);
        var parts = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var building = parts.Length > 1 ? parts[0] : "根目录";
        var fileStem = Path.GetFileNameWithoutExtension(path);
        var match = TransformerRegex().Match(Path.GetFileName(path));
        var transformer = match.Success ? match.Value : fileStem;
        return new NativeLocation(building, transformer);
    }

    [GeneratedRegex("(\\d+TM[a-zA-Z]*\\d*|充电桩)", RegexOptions.CultureInvariant)]
    private static partial Regex TransformerRegex();

    [GeneratedRegex("(\\d{4}[_\\-]?[01]\\d[_\\-]?[0-3]\\d|\\d{8})", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    private static IReadOnlyList<ReportDeviceSummary> BuildDeviceSummaries(
        IReadOnlyList<ReportDetailPreview> details,
        CancellationToken cancellationToken)
    {
        return details
            .GroupBy(
                static detail => new
                {
                    Building = string.IsNullOrWhiteSpace(detail.Building) ? "根目录" : detail.Building,
                    DevicePath = DevicePathFromRelativePath(detail.RelativePath),
                    Transformer = string.IsNullOrWhiteSpace(detail.Transformer) ? "未知" : detail.Transformer,
                })
            .Select(group =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var severities = group.Select(static detail => detail.Severity ?? "").ToList();
                var issueTypes = group
                    .SelectMany(static detail => SplitIssueTypes(detail.IssueType))
                    .GroupBy(static issueType => issueType, StringComparer.Ordinal)
                    .Select(static issueGroup => new { Name = issueGroup.Key, Count = issueGroup.Count() })
                    .OrderByDescending(static issue => issue.Count)
                    .Take(5)
                    .Select(static issue => $"{issue.Name}({issue.Count})")
                    .ToList();
                var highestSeverity = severities.Contains("高", StringComparer.Ordinal)
                    ? "高"
                    : severities.Contains("中", StringComparer.Ordinal)
                        ? "中"
                        : "低";
                return new ReportDeviceSummary
                {
                    Building = group.Key.Building,
                    DevicePath = group.Key.DevicePath,
                    Transformer = group.Key.Transformer,
                    AnomalyRecords = group.Count(),
                    MainIssueTypes = string.Join("；", issueTypes),
                    HighestSeverity = highestSeverity,
                    Priority = PriorityForSeverity(highestSeverity),
                };
            })
            .OrderBy(static device => PriorityRank(device.Priority))
            .ThenByDescending(static device => device.AnomalyRecords)
            .ToList();
    }

    private static List<ReportIssueType> BuildTopIssueTypes(IReadOnlyList<ReportDetailPreview> details)
    {
        return BuildIssueTypeStatistics(details)
            .Take(8)
            .ToList();
    }

    private static List<ReportIssueType> BuildIssueTypeStatistics(IReadOnlyList<ReportDetailPreview> details)
    {
        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var issueType in details.SelectMany(static detail => SplitIssueTypes(detail.IssueType)))
        {
            if (!counts.ContainsKey(issueType))
            {
                counts[issueType] = 0;
                order.Add(issueType);
            }

            counts[issueType]++;
        }

        return order
            .Select(name => new ReportIssueType { Name = name, Count = counts[name] })
            .OrderByDescending(static issue => issue.Count)
            .ToList();
    }

    private static string FirstDetailTime(
        IReadOnlyList<ReportDetailPreview> details,
        string building,
        string devicePath,
        string transformer) =>
        ChronologicalDetailTime(details, building, devicePath, transformer, earliest: true);

    private static string LastDetailTime(
        IReadOnlyList<ReportDetailPreview> details,
        string building,
        string devicePath,
        string transformer) =>
        ChronologicalDetailTime(details, building, devicePath, transformer, earliest: false);

    private static string ChronologicalDetailTime(
        IReadOnlyList<ReportDetailPreview> details,
        string building,
        string devicePath,
        string transformer,
        bool earliest)
    {
        var matching = details
            .Where(detail => DetailMatchesDevice(detail, building, devicePath, transformer))
            .ToList();
        var parsed = matching
            .Select((detail, index) => TryParseDetailTimestamp(detail, out var timestamp)
                ? (Detail: detail, Timestamp: (DateTime?)timestamp, Index: index)
                : (Detail: detail, Timestamp: (DateTime?)null, Index: index))
            .Where(static item => item.Timestamp.HasValue)
            .ToList();
        if (parsed.Count > 0)
        {
            var selected = earliest
                ? parsed.MinBy(static item => (item.Timestamp!.Value, item.Index))
                : parsed.MaxBy(static item => (item.Timestamp!.Value, -item.Index));
            return selected.Detail.TimeText;
        }

        var fallback = matching
            .Select(static detail => detail.TimeText)
            .Where(static value => !string.IsNullOrWhiteSpace(value));
        return (earliest ? fallback.FirstOrDefault() : fallback.LastOrDefault()) ?? "";
    }

    private static bool TryParseDetailTimestamp(
        ReportDetailPreview detail,
        out DateTime timestamp)
    {
        var text = string.Join(
            " ",
            new[] { detail.Date, detail.Time }.Where(static value => !string.IsNullOrWhiteSpace(value)));
        return DateTime.TryParse(
                   text,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces,
                   out timestamp)
               || DateTime.TryParse(
                   text,
                   CultureInfo.CurrentCulture,
                   DateTimeStyles.AllowWhiteSpaces,
                   out timestamp);
    }

    private static bool DetailMatchesDevice(
        ReportDetailPreview detail,
        string building,
        string devicePath,
        string transformer) =>
        string.Equals(
            string.IsNullOrWhiteSpace(detail.Building) ? "根目录" : detail.Building,
            building,
            StringComparison.Ordinal)
        && string.Equals(DevicePathFromRelativePath(detail.RelativePath), devicePath, StringComparison.Ordinal)
        && string.Equals(
            string.IsNullOrWhiteSpace(detail.Transformer) ? "未知" : detail.Transformer,
            transformer,
            StringComparison.Ordinal);

    private static string DevicePathFromRelativePath(string? relativePath)
    {
        var raw = (relativePath ?? "").Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(raw)
            || raw.StartsWith("/", StringComparison.Ordinal)
            || (raw.Length >= 2 && char.IsLetter(raw[0]) && raw[1] == ':'))
        {
            return "根目录";
        }

        var parts = raw
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static part => part != ".")
            .ToArray();
        if (parts.Length <= 1 || parts.Any(static part => part == ".."))
        {
            return "根目录";
        }

        return string.Join('/', parts[..^1]);
    }

    private static IEnumerable<string> SplitIssueTypes(string? issueTypes) =>
        (issueTypes ?? "")
            .Split([';', '；', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string PriorityForSeverity(string severity) =>
        severity switch
        {
            "高" => "P1",
            "中" => "P2",
            _ => "P3",
        };

    private static int PriorityRank(string priority) =>
        priority switch
        {
            "P1" => 0,
            "P2" => 1,
            "P3" => 2,
            _ => 3,
        };

    private static int SeverityRank(string? severity) =>
        severity switch
        {
            "高" => 0,
            "中" => 1,
            "低" => 2,
            _ => 3,
        };

}
