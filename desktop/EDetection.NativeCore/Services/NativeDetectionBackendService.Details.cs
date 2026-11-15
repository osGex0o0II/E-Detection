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
    private static void AddDetailPreview(
        List<ReportDetailPreview> target,
        IReadOnlyList<ReportDetailPreview> details,
        string relativePath)
    {
        foreach (var detail in details)
        {
            detail.RelativePath ??= relativePath;
            target.Add(detail);
        }
    }

    private static Dictionary<string, double> BuildRowVoltageValues(
        IReadOnlyList<string> cells,
        IReadOnlyList<NativeColumnGroup> voltageColumnGroups)
    {
        return BuildRowNumericValues(cells, voltageColumnGroups);
    }

    private static Dictionary<string, double> BuildRowNumericValues(
        IReadOnlyList<string> cells,
        IReadOnlyList<NativeColumnGroup> columnGroups)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (name, value) in BuildRowMergedNumericValues(cells, columnGroups))
        {
            if (!IsInvalidValue(value))
            {
                values[name] = value;
            }
        }

        return values;
    }

    private static IReadOnlyList<string> BuildDetailPreviewIssueTypes(
        IReadOnlyList<string> rowTypes,
        NativeRowAnalysis row,
        NativeDetectionConfig config)
    {
        if (!config.DetailOutputEnabled)
        {
            return rowTypes;
        }

        var detailTypes = new List<string>();
        var detailedPtTypes = rowTypes
            .Where(static type => TryGetDetailedPtIssue(type, out _, out _))
            .ToList();
        if (detailedPtTypes.Count > 0)
        {
            detailTypes.Add(string.Join(
                "; ",
                detailedPtTypes
                    .Select(static type => TryGetDetailedPtIssue(type, out var compactType, out _) ? compactType : type)
                    .Concat(rowTypes.Where(static type => TryGetDetailedVoltageIssue(type, out _, out _, out _)))
                    .Distinct(StringComparer.Ordinal)));
            detailTypes.AddRange(rowTypes.Where(static type =>
                !TryGetDetailedPtIssue(type, out _, out _)
                && !TryGetDetailedVoltageIssue(type, out _, out _, out _)
                && !TryGetDetailedImbalanceIssue(type, out _, out _)));
            return detailTypes.Distinct(StringComparer.Ordinal).ToList();
        }

        foreach (var detailType in new[] { "电压过低", "电压过高" })
        {
            var phaseTypes = VoltagePhaseColumns()
                .Select(phaseColumn => $"{phaseColumn.Phase}{detailType}")
                .Where(rowTypes.Contains)
                .ToList();
            if (phaseTypes.Count == 0)
            {
                continue;
            }

            detailTypes.Add(CompactDetailedVoltageIssueType(detailType, phaseTypes, row));
        }

        detailTypes.AddRange(rowTypes
            .Where(static type => !TryGetDetailedVoltageIssue(type, out _, out _, out _))
            .Select(static type => TryGetDetailedImbalanceIssue(type, out var compactType, out _) ? compactType : type));
        return detailTypes.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string BuildCompactIssueType(IReadOnlyList<string> issueTypes)
    {
        var groups = GroupIssueTypes(issueTypes);
        var result = new List<string>();
        foreach (var group in groups.OrderByDescending(static group => group.Value.Count))
        {
            var root = group.Key;
            var items = group.Value;
            if (items.Count == 1)
            {
                result.Add(CleanIssueType(items[0]));
                continue;
            }

            var phases = items
                .Select(GetIssuePhase)
                .Where(static phase => !string.IsNullOrWhiteSpace(phase))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
            result.Add(phases.Count > 0 ? $"{root}({string.Join("/", phases)}相)" : root);
        }

        return result.Count <= 3
            ? string.Join("; ", result)
            : string.Join("; ", result.Take(3).Append($"+{result.Count - 3}类"));
    }

    private static string BuildCompactIssueDetail(IReadOnlyList<string> issueTypes)
    {
        var result = new List<string>();
        foreach (var group in GroupIssueTypes(issueTypes))
        {
            var items = group.Value;
            var parts = items.Select(item =>
            {
                var phase = GetIssuePhase(item);
                var parenthesis = Regex.Match(item, "\\(([^)]*)\\)");
                if (!string.IsNullOrWhiteSpace(phase) && parenthesis.Success)
                {
                    return $"{phase}{parenthesis.Groups[1].Value}";
                }

                if (parenthesis.Success)
                {
                    return parenthesis.Groups[1].Value;
                }

                return CleanIssueType(item);
            }).ToList();

            result.Add(items.Count == 1 && string.IsNullOrWhiteSpace(GetIssuePhase(parts[0]))
                ? parts[0]
                : $"{group.Key}: {string.Join(", ", parts)}");
        }

        return string.Join(" | ", result);
    }

    private static string BuildCompactIssueValue(IReadOnlyList<string> issueTypes, NativeRowAnalysis row) =>
        string.Join(
            ", ",
            ReportValueColumns
                .Where(column => row.ReportValues.ContainsKey(column)
                    && issueTypes.Any(type => type.Contains(column, StringComparison.Ordinal)
                        || (PhaseMap.TryGetValue(column, out var phase) && type.Contains(phase, StringComparison.Ordinal))))
                .Select(column => $"{column}={row.ReportValues[column].ToString("F1", CultureInfo.InvariantCulture)}")
                .Take(8));

    // These two helpers preserve the established structured-detail and
    // summary sequence. It is tempting
    // to summarize the already-compacted type labels, which loses the original
    // long-standing behaviour for a row containing multiple issue families:
    // its structured detail uses " | ", which remains one summary token.
    private static string BuildStructuredIssueDetail(IEnumerable<string> issueTypes)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var issueType in issueTypes)
        {
            var root = Regex.Replace(CleanIssueType(issueType), "[ABC]相", "").Trim();
            if (!groups.TryGetValue(root, out var items))
            {
                items = [];
                groups.Add(root, items);
            }

            items.Add(issueType);
        }

        var result = new List<string>();
        foreach (var (root, items) in groups)
        {
            var parts = new List<string>();
            foreach (var item in items)
            {
                var phase = GetIssuePhase(item);
                var parenthesis = Regex.Match(item, "\\(([^)]*)\\)");
                if (!string.IsNullOrWhiteSpace(phase) && parenthesis.Success)
                {
                    parts.Add($"{phase}相{parenthesis.Groups[1].Value}");
                }
                else if (parenthesis.Success)
                {
                    parts.Add(parenthesis.Groups[1].Value);
                }
                else
                {
                    parts.Add(CleanIssueType(item));
                }
            }

            result.Add(parts.Count == 1 && !Regex.IsMatch(parts[0], "[ABC]相")
                ? parts[0]
                : $"{root}: {string.Join(", ", parts)}");
        }

        return string.Join(" | ", result);
    }

    private static string BuildFileAnomalyTypes(IEnumerable<string> structuredDetails)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var detail in structuredDetails)
        {
            // The summary pipeline splits on semicolons only: a structured
            // "A | B" detail deliberately remains one summary token.
            foreach (var part in detail.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var clean = CleanIssueType(part);
                var root = Regex.Replace(clean, "[ABC]相", "").Trim();
                if (!groups.TryGetValue(root, out var items))
                {
                    items = [];
                    groups.Add(root, items);
                }

                items.Add(part);
            }
        }

        var result = new List<string>();
        foreach (var (root, items) in groups.OrderByDescending(static pair => pair.Value.Count))
        {
            if (items.Count == 1)
            {
                result.Add(CleanIssueType(items[0]));
                continue;
            }

            var phases = items
                .Select(GetIssuePhase)
                .Where(static phase => !string.IsNullOrWhiteSpace(phase))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
            result.Add(phases.Count > 0 ? $"{root}({string.Join("/", phases)}相)" : root);
        }

        return result.Count <= 3
            ? string.Join("; ", result)
            : string.Join("; ", result.Take(3).Append($"+{result.Count - 3}类"));
    }

    // A value (rather than merely "1") is used as the HMAC key so the hashes
    // cannot be dictionary-reversed from the small, known issue-label
    // vocabulary.  Operators comparing separate backends must supply the same
    // locally held random key to both processes.
    private static string? GetParityTraceKey()
    {
        var value = Environment.GetEnvironmentVariable("EDETECTION_NATIVE_PARITY_TRACE");
        return string.IsNullOrWhiteSpace(value)
               || value.Equals("0", StringComparison.Ordinal)
               || value.Equals("false", StringComparison.OrdinalIgnoreCase)
               || value.Equals("off", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;
    }

    private static NativeParityTrace BuildUnavailableParityTrace(string key) =>
        new()
        {
            AnalysisState = "not_analyzed",
            FinalAnomalyTypesHash = ComputeParityTraceHash(key, "not-analyzed"),
        };

    private static NativeParityTrace? BuildParityTrace(
        string? key,
        int dataRowCount,
        IReadOnlyList<IReadOnlyList<string>>? rawRowLabels,
        IReadOnlyList<IReadOnlyList<string>>? reportableRowLabels,
        IReadOnlyList<string> structuredDetails,
        string anomalyTypes)
    {
        if (key is null)
        {
            return null;
        }

        var rawRows = rawRowLabels ?? [];
        var reportableRows = reportableRowLabels ?? [];
        return new NativeParityTrace
        {
            DataRowCount = dataRowCount,
            RawRowLabelCount = rawRows.Sum(static labels => labels.Count),
            ReportableRowLabelCount = reportableRows.Sum(static labels => labels.Count),
            StructuredDetailCount = structuredDetails.Count,
            RawRowLabelHashes = HashParityTraceRows(key, "raw", rawRows),
            ReportableRowLabelHashes = HashParityTraceRows(key, "reportable", reportableRows),
            StructuredDetailHashes = structuredDetails
                .Select((detail, index) => ComputeParityTraceHash(key, $"detail\u001f{index}\u001f{detail}"))
                .ToList(),
            FinalAnomalyTypesHash = ComputeParityTraceHash(key, $"summary\u001f{anomalyTypes}"),
        };
    }

    private static List<string> HashParityTraceRows(
        string key,
        string stage,
        IReadOnlyList<IReadOnlyList<string>> rows) =>
        rows.Select((labels, index) => ComputeParityTraceHash(
                key,
                $"{stage}\u001f{index}\u001f{string.Join("\u001e", labels)}"))
            .ToList();

    private static string ComputeParityTraceHash(string key, string payload)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        try
        {
            return Convert.ToHexStringLower(HMACSHA256.HashData(keyBytes, payloadBytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(payloadBytes);
        }
    }

    private static Dictionary<string, List<string>> GroupIssueTypes(IEnumerable<string> issueTypes)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var issueType in issueTypes)
        {
            var root = Regex.Replace(CleanIssueType(issueType), "[ABC]相", "");
            if (!groups.TryGetValue(root, out var items))
            {
                items = [];
                groups.Add(root, items);
            }

            items.Add(issueType);
        }

        return groups;
    }

    private static string CleanIssueType(string issueType)
    {
        var clean = Regex.Replace(issueType, "\\([^)]*\\)", "").Trim();
        var colon = clean.IndexOf(':');
        return colon >= 0 ? clean[..colon].Trim() : clean;
    }

    private static string GetIssuePhase(string issueType) =>
        issueType.StartsWith("A相", StringComparison.Ordinal) ? "A"
        : issueType.StartsWith("B相", StringComparison.Ordinal) ? "B"
        : issueType.StartsWith("C相", StringComparison.Ordinal) ? "C"
        : "";

    private static int IssueRuleOrder(string issueType) =>
        issueType.Contains("电压", StringComparison.Ordinal) || issueType.Contains("PT接线", StringComparison.Ordinal) ? 0
        : issueType.Contains("电流过大", StringComparison.Ordinal) ? 1
        : issueType.Contains("电流不平衡", StringComparison.Ordinal) ? 2
        : IsFreezeOnlyType(issueType) ? 3
        : issueType.Contains("CT", StringComparison.Ordinal) || issueType.Contains("有功功率", StringComparison.Ordinal) ? 4
        : issueType.Contains("功率因数", StringComparison.Ordinal) ? 5
        : issueType.Contains("温度", StringComparison.Ordinal) ? 6
        : 7;

    private static Dictionary<string, double> BuildRowMergedNumericValues(
        IReadOnlyList<string> cells,
        IReadOnlyList<NativeColumnGroup> columnGroups)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var group in columnGroups)
        {
            var numericValues = new List<double>();
            foreach (var index in group.Indexes)
            {
                if (index < cells.Count && TryParseDouble(cells[index], out var parsed))
                {
                    numericValues.Add(parsed);
                }
            }

            if (numericValues.Count > 0)
            {
                values[group.Name] = numericValues.Average();
            }
        }

        return values;
    }

    private static Dictionary<string, double> BuildReportValues(
        params IReadOnlyDictionary<string, double>[] valueSets)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var valueSet in valueSets)
        {
            foreach (var (key, value) in valueSet)
            {
                values[key] = value;
            }
        }

        return values;
    }

    private static string BuildRowIssueDetail(string issueType, NativeRowAnalysis row, NativeDetectionConfig config) =>
        config.DetailOutputEnabled && issueType.Contains("电压疑似PT接线异常", StringComparison.Ordinal)
            ? BuildDetailedPtIssueDetail(issueType, row)
            : config.DetailOutputEnabled && TryGetDetailedImbalanceIssue(issueType, out _, out _)
            ? BuildDetailedImbalanceIssueDetail(issueType)
            : config.DetailOutputEnabled && TryGetCompactDetailedVoltageIssue(issueType, out var compactDetailType)
            ? BuildCompactVoltageIssueDetail(issueType, compactDetailType, row)
            : config.DetailOutputEnabled && TryGetDetailedVoltageIssue(issueType, out var voltageColumn, out var phase, out var detailType)
            && row.ReportValues.TryGetValue(voltageColumn, out var voltageValue)
            ? $"{detailType}: {phase}{voltageValue.ToString("F1", CultureInfo.InvariantCulture)}V"
            : issueType;

    private static string BuildIssueValue(string issueType, NativeRowAnalysis row, NativeDetectionConfig config)
    {
        if (config.DetailOutputEnabled
            && issueType.Contains("PT接线异常", StringComparison.Ordinal))
        {
            return "";
        }

        if (config.DetailOutputEnabled
            && TryBuildDetailedImbalanceIssueValue(issueType, row, out var imbalanceValue))
        {
            return imbalanceValue;
        }

        if (!config.DetailOutputEnabled
            || TryGetDetailedVoltageIssue(issueType, out _, out _, out _)
            || TryGetCompactDetailedVoltageIssue(issueType, out _))
        {
            return "";
        }

        return string.Join(
            ", ",
            ReportValueColumns
                .Where(column => row.ReportValues.ContainsKey(column)
                    && (issueType.Contains(column, StringComparison.Ordinal)
                        || (PhaseMap.TryGetValue(column, out var phase)
                            && issueType.Contains(phase, StringComparison.Ordinal))))
                .Select(column => $"{column}={row.ReportValues[column].ToString("F1", CultureInfo.InvariantCulture)}"));
    }

    private static string SummaryIssueType(string issueType) =>
        issueType.Contains("电压疑似PT接线异常", StringComparison.Ordinal)
            ? "电压疑似PT接线异常"
            : TryGetDetailedImbalanceIssue(issueType, out _, out _)
                ? "电压电压不平衡"
            : TryGetCompactDetailedVoltageIssue(issueType, out var compactDetailType)
                ? compactDetailType
                : TryGetDetailedVoltageIssue(issueType, out _, out _, out var detailType)
                    ? detailType
                    : issueType;

    private static string CompactDetailedVoltageIssueType(
        string detailType,
        IReadOnlyList<string> phaseTypes,
        NativeRowAnalysis row)
    {
        if (phaseTypes.Count == 1)
        {
            return phaseTypes[0];
        }

        var phases = VoltagePhaseColumns()
            .Where(phaseColumn => phaseTypes.Contains($"{phaseColumn.Phase}{detailType}")
                                  && row.ReportValues.ContainsKey(phaseColumn.Column))
            .Select(static phaseColumn => phaseColumn.Phase[0].ToString())
            .ToList();
        return phases.Count > 0
            ? $"{detailType}({string.Join("/", phases)}相)"
            : detailType;
    }

    private static bool TryGetCompactDetailedVoltageIssue(string issueType, out string detailType)
    {
        if (issueType.StartsWith("电压过低(", StringComparison.Ordinal)
            || issueType == "电压过低")
        {
            detailType = "电压过低";
            return true;
        }

        if (issueType.StartsWith("电压过高(", StringComparison.Ordinal)
            || issueType == "电压过高")
        {
            detailType = "电压过高";
            return true;
        }

        detailType = "";
        return false;
    }

    private static string BuildCompactVoltageIssueDetail(string issueType, string detailType, NativeRowAnalysis row)
    {
        var phases = CompactDetailedVoltageIssuePhases(issueType, detailType)
            .ToHashSet(StringComparer.Ordinal);
        var values = VoltagePhaseColumns()
            .Where(phaseColumn => phases.Contains(phaseColumn.Phase)
                                  && row.ReportValues.ContainsKey(phaseColumn.Column))
            .Select(phaseColumn =>
                $"{phaseColumn.Phase}{row.ReportValues[phaseColumn.Column].ToString("F1", CultureInfo.InvariantCulture)}V")
            .ToList();
        return values.Count > 0
            ? $"{detailType}: {string.Join(", ", values)}"
            : detailType;
    }

    private static string BuildDetailedPtIssueDetail(string issueType, NativeRowAnalysis row)
    {
        var details = new List<string>();
        var ptValues = SplitIssueTypes(issueType)
            .Select(type =>
            {
                if (!TryGetDetailedPtIssue(type, out var compactType, out var detailValue))
                {
                    return "";
                }

                return string.IsNullOrWhiteSpace(detailValue)
                    ? BuildCompactPtDetailValue(compactType, row)
                    : detailValue;
            })
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        if (ptValues.Count > 0)
        {
            details.Add($"电压疑似PT接线异常: {string.Join(", ", ptValues)}");
        }

        foreach (var detailType in new[] { "电压过低", "电压过高" })
        {
            var phaseValues = SplitIssueTypes(issueType)
                .Where(type => TryGetDetailedVoltageIssue(type, out _, out _, out var voltageDetailType)
                               && voltageDetailType == detailType)
                .Select(type => TryGetDetailedVoltageIssue(type, out var voltageColumn, out var phase, out _)
                                && row.ReportValues.TryGetValue(voltageColumn, out var voltageValue)
                    ? $"{phase}{voltageValue.ToString("F1", CultureInfo.InvariantCulture)}V"
                    : "")
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            if (phaseValues.Count > 0)
            {
                details.Add($"{detailType}: {string.Join(", ", phaseValues)}");
            }
        }

        return details.Count > 0 ? string.Join(" | ", details) : issueType;
    }

    private static string BuildCompactPtDetailValue(string compactType, NativeRowAnalysis row)
    {
        foreach (var (phase, column) in VoltagePhaseColumns())
        {
            if (compactType != $"{phase}电压疑似PT接线异常"
                || !row.ReportValues.TryGetValue(column, out var value))
            {
                continue;
            }

            var voltageValues = VoltagePhaseColumns()
                .Where(phaseColumn => row.ReportValues.ContainsKey(phaseColumn.Column))
                .Select(phaseColumn => row.ReportValues[phaseColumn.Column])
                .ToList();
            var median = Median(voltageValues);
            if (median == 0)
            {
                return "";
            }

            var deviation = Math.Abs(value - median) / median * 100.0;
            return $"{phase}偏差{deviation.ToString("F1", CultureInfo.InvariantCulture)}%";
        }

        return "";
    }

    private static string BuildDetailedImbalanceIssueDetail(string issueType)
    {
        var values = SplitIssueTypes(issueType)
            .Select(static type => TryGetDetailedImbalanceIssue(type, out var compactType, out _) ? compactType : "")
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        return values.Count > 0
            ? $"电压电压不平衡: {string.Join(", ", values)}"
            : issueType;
    }

    private static bool TryBuildDetailedImbalanceIssueValue(
        string issueType,
        NativeRowAnalysis row,
        out string issueValue)
    {
        var values = SplitIssueTypes(issueType)
            .Select(type =>
            {
                if (!TryGetDetailedImbalanceIssue(type, out _, out var voltageColumn)
                    || !row.ReportValues.TryGetValue(voltageColumn, out var value))
                {
                    return "";
                }

                return $"{voltageColumn}={value.ToString("F1", CultureInfo.InvariantCulture)}";
            })
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        issueValue = string.Join(", ", values);
        return values.Count > 0;
    }

    private static IReadOnlyList<string> CompactDetailedVoltageIssuePhases(string issueType, string detailType)
    {
        if (issueType == detailType)
        {
            return VoltagePhaseColumns()
                .Select(static phaseColumn => phaseColumn.Phase)
                .ToList();
        }

        if (!issueType.StartsWith($"{detailType}(", StringComparison.Ordinal)
            || !issueType.EndsWith("相)", StringComparison.Ordinal))
        {
            return [];
        }

        var phaseText = issueType.Substring(detailType.Length + 1, issueType.Length - detailType.Length - 3);
        var phaseSet = phaseText
            .Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(static phase => $"{phase}相")
            .ToHashSet(StringComparer.Ordinal);
        return VoltagePhaseColumns()
            .Where(phaseColumn => phaseSet.Contains(phaseColumn.Phase))
            .Select(static phaseColumn => phaseColumn.Phase)
            .ToList();
    }

    private static bool TryGetDetailedPtIssue(string issueType, out string compactType, out string detailValue)
    {
        foreach (var (phase, _) in VoltagePhaseColumns())
        {
            var prefix = $"{phase}电压疑似PT接线异常(偏差";
            if (issueType.StartsWith(prefix, StringComparison.Ordinal)
                && issueType.EndsWith("%)", StringComparison.Ordinal))
            {
                var percent = issueType.Substring(prefix.Length, issueType.Length - prefix.Length - 2);
                compactType = $"{phase}电压疑似PT接线异常";
                detailValue = $"{phase}偏差{percent}%";
                return true;
            }

            if (issueType == $"{phase}电压疑似PT接线异常")
            {
                compactType = issueType;
                detailValue = "";
                return true;
            }
        }

        compactType = "";
        detailValue = "";
        return false;
    }

    private static bool TryGetDetailedImbalanceIssue(string issueType, out string compactType, out string voltageColumn)
    {
        foreach (var (phase, column) in VoltagePhaseColumns())
        {
            var compact = $"{phase}电压电压不平衡";
            if (issueType == compact || issueType.StartsWith($"{compact}:", StringComparison.Ordinal))
            {
                compactType = compact;
                voltageColumn = column;
                return true;
            }
        }

        compactType = "";
        voltageColumn = "";
        return false;
    }

    private static bool TryGetDetailedVoltageIssue(
        string issueType,
        out string voltageColumn,
        out string phase,
        out string detailType)
    {
        foreach (var (prefix, column) in VoltagePhaseColumns())
        {
            if (issueType == $"{prefix}电压过低")
            {
                voltageColumn = column;
                phase = prefix;
                detailType = "电压过低";
                return true;
            }

            if (issueType == $"{prefix}电压过高")
            {
                voltageColumn = column;
                phase = prefix;
                detailType = "电压过高";
                return true;
            }
        }

        voltageColumn = "";
        phase = "";
        detailType = "";
        return false;
    }

    private static IReadOnlyList<(string Phase, string Column)> VoltagePhaseColumns() =>
    [
        ("A相", "Uab"),
        ("B相", "Ubc"),
        ("C相", "Uca"),
    ];

    private static string PhaseForVoltageColumn(string column) =>
        VoltagePhaseColumns()
            .FirstOrDefault(phaseColumn => phaseColumn.Column == column)
            .Phase ?? column;

    private static string PhaseForCurrentColumn(string column) =>
        column switch
        {
            "Ia" => "A相",
            "Ib" => "B相",
            "Ic" => "C相",
            _ => column,
        };

}
