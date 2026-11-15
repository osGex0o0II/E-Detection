using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using EDetection.NativeCore.Detection;
using EDetection.NativeCore.Models;

namespace EDetection.NativeCore.Services;

public sealed partial class NativeDetectionBackendService : IDetectionBackendService
{
    private static NativeFileAnalysis AnalyzeFile(
        string path,
        string fileName,
        string relativePath,
        NativeLocation location,
        NativeDetectionConfig config,
        string? parityTraceKey,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> lines;
        try
        {
            lines = ReadCsvLines(path, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or DecoderFallbackException
                                   or ArgumentException
                                   or NotSupportedException
                                   or PathTooLongException)
        {
            return NativeFileAnalysis.Skipped(
                $"跳过空文件或读取失败: {fileName} ({ex.GetType().Name})");
        }

        if (lines.Count < 2)
        {
            return NativeFileAnalysis.Skipped($"跳过空文件或读取失败: {fileName}");
        }

        var headers = SplitCsvLine(lines[0]);
        var columns = headers
            .Select((header, index) => new
            {
                Name = NormalizeColumnName(header),
                Raw = header.Trim(),
                Index = index,
            })
            .ToList();
        if (!columns.Any(static column => IsKeyParameterColumn(column.Name)))
        {
            return NativeFileAnalysis.Skipped($"跳过：未匹配到关键参数。当前列：{string.Join(", ", headers.Take(5))}...");
        }

        // Two different source headers that resolve to the same measurement
        // cannot be told apart.  Averaging them would blend distinct physical
        // quantities into a reading no sensor ever produced, so refuse to issue
        // a verdict on the file instead of reporting a fabricated value.
        var ambiguousColumns = columns
            .Where(static column => IsRuleEvaluatedColumn(column.Name))
            .GroupBy(static column => column.Name, StringComparer.Ordinal)
            .Select(static group => new
            {
                group.Key,
                RawHeaders = group
                    .Select(static column => column.Raw)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            })
            .Where(static group => group.RawHeaders.Count > 1)
            .Select(static group => $"{group.Key}({string.Join("/", group.RawHeaders)})")
            .ToList();
        if (ambiguousColumns.Count > 0)
        {
            return NativeFileAnalysis.Skipped(
                $"跳过：列名歧义，无法确定物理量。冲突列：{string.Join("，", ambiguousColumns)}");
        }

        var timeIndex = FindTimeColumnIndex(headers, out var matchedTimeHeader);
        var voltageColumnGroups = columns
            .Where(static column => IsVoltageColumn(column.Name))
            .GroupBy(static column => column.Name, StringComparer.Ordinal)
            .Select(static group => new NativeColumnGroup(
                group.Key,
                group.Select(static column => column.Index).ToList()))
            .ToList();
        var currentColumnGroups = columns
            .Where(static column => IsCurrentColumn(column.Name))
            .GroupBy(static column => column.Name, StringComparer.Ordinal)
            .Select(static group => new NativeColumnGroup(
                group.Key,
                group.Select(static column => column.Index).ToList()))
            .ToList();
        var activePowerColumnGroups = columns
            .Where(static column => column.Name == "有功功率")
            .GroupBy(static column => column.Name, StringComparer.Ordinal)
            .Select(static group => new NativeColumnGroup(
                group.Key,
                group.Select(static column => column.Index).ToList()))
            .ToList();
        var powerFactorColumnGroups = columns
            .Where(static column => column.Name == "功率因数")
            .GroupBy(static column => column.Name, StringComparer.Ordinal)
            .Select(static group => new NativeColumnGroup(
                group.Key,
                group.Select(static column => column.Index).ToList()))
            .ToList();
        var reactivePowerColumnGroups = columns
            .Where(static column => column.Name == "无功功率")
            .GroupBy(static column => column.Name, StringComparer.Ordinal)
            .Select(static group => new NativeColumnGroup(
                group.Key,
                group.Select(static column => column.Index).ToList()))
            .ToList();
        var temperatureColumnGroups = columns
            .Where(static column => IsTemperatureColumn(column.Name))
            .GroupBy(static column => column.Name, StringComparer.Ordinal)
            .Select(static group => new NativeColumnGroup(
                group.Key,
                group.Select(static column => column.Index).ToList()))
            .ToList();
        if (voltageColumnGroups.Count == 0 && currentColumnGroups.Count == 0)
        {
            return NativeFileAnalysis.Normal(fileName);
        }

        var anomalyRows = 0;
        // Derive a file's anomaly_types from the final structured
        // per-row "异常详情" text.  Keep the original row labels until the
        // file is complete so a mixed row (for example voltage + frozen data)
        // retains the same composite summary as the established detection contract.
        var anomalyTypeDetails = new List<string>();
        var rowAnalyses = new List<NativeRowAnalysis>();
        // The trace is intentionally populated only in diagnostic mode.  The
        // lists retain label order and row boundaries until they are HMACed;
        // no plaintext leaves this method.
        List<IReadOnlyList<string>>? rawRowLabels = parityTraceKey is null ? null : [];
        List<IReadOnlyList<string>>? reportableRowLabels = parityTraceKey is null ? null : [];
        var detailPreview = new List<ReportDetailPreview>();
        var keyColumnGroups = voltageColumnGroups
            .Concat(currentColumnGroups)
            .ToList();
        var offlineInvalidCounts = keyColumnGroups.ToDictionary(
            static group => group.Name,
            static _ => 0,
            StringComparer.Ordinal);
        var sensorColumnGroups = reactivePowerColumnGroups
            .Concat(powerFactorColumnGroups)
            .Concat(temperatureColumnGroups)
            .ToList();
        var sensorStates = sensorColumnGroups.ToDictionary(
            static group => group.Name,
            static _ => new NativeSensorColumnState(),
            StringComparer.Ordinal);
        var ruleOptions = ToRuleOptions(config);
        var dataRows = 0;
        foreach (var line in lines.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cells = SplitCsvLine(line);
            if (matchedTimeHeader && timeIndex < cells.Count && IsStatisticsTailRow(cells[timeIndex]))
            {
                // A statistics footer is not telemetry.  Drop this row alone -
                // never the remainder of the file - so a label appearing
                // mid-file cannot silently truncate the data set.
                continue;
            }

            var rowTime = timeIndex < cells.Count ? cells[timeIndex] : "";
            dataRows++;
            var rowVoltageValues = BuildRowVoltageValues(cells, voltageColumnGroups);
            var rowCurrentValues = BuildRowNumericValues(cells, currentColumnGroups);
            var rowActivePowerValues = BuildRowNumericValues(cells, activePowerColumnGroups);
            var rowPowerFactorValues = BuildRowNumericValues(cells, powerFactorColumnGroups);
            var rowTemperatureValues = BuildRowNumericValues(cells, temperatureColumnGroups);
            UpdateOfflineInvalidCounts(cells, voltageColumnGroups, offlineInvalidCounts);
            UpdateOfflineInvalidCounts(cells, currentColumnGroups, offlineInvalidCounts);
            // pandas' fillna(0) treats blank sensor cells as zero when
            // deciding whether a configured sensor is missing. Observe every
            // configured sensor for every data row, including blank cells.
            UpdateSensorStates(cells, sensorColumnGroups, sensorStates);
            var rowFreezeCoreValues = BuildRowNumericValues(
                cells,
                voltageColumnGroups
                    .Concat(currentColumnGroups)
                    .Concat(activePowerColumnGroups)
                    .ToList());
            var rowFreezeAuxValues = BuildRowNumericValues(
                cells,
                reactivePowerColumnGroups
                    .Concat(powerFactorColumnGroups)
                    .ToList());
            var rowTypes = DetectionRowRulePipeline.Default
                .Evaluate(
                    new DetectionRowContext(
                        rowVoltageValues,
                        rowCurrentValues,
                        rowActivePowerValues,
                        rowPowerFactorValues,
                        rowTemperatureValues,
                        voltageColumnGroups.Count),
                    ruleOptions)
                .ToList();
            rawRowLabels?.Add(rowTypes.ToList());
            var reportValues = BuildReportValues(
                rowVoltageValues,
                rowCurrentValues,
                rowActivePowerValues,
                rowPowerFactorValues,
                rowTemperatureValues,
                BuildRowNumericValues(cells, reactivePowerColumnGroups));
            rowAnalyses.Add(new NativeRowAnalysis(rowTime, rowTypes, rowFreezeCoreValues, rowFreezeAuxValues, reportValues));
        }

        var freezeRows = rowAnalyses
            .Select(static row => new FreezeDetectionRow(row.FreezeCoreValues, row.FreezeAuxValues))
            .ToList();
        var freezeRowTypes = FreezeRowDetector.Detect(freezeRows, ruleOptions, cancellationToken);
        for (var rowIndex = 0; rowIndex < rowAnalyses.Count; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowTypes = rowAnalyses[rowIndex]
                .AnomalyTypes
                .Concat(freezeRowTypes[rowIndex])
                .ToList();
            if (rowTypes.Count == 0 || rowTypes.All(IsFreezeOnlyType))
            {
                continue;
            }

            var reportableRowTypes = rowTypes
                .OrderBy(IssueRuleOrder)
                .ToList();
            reportableRowLabels?.Add(reportableRowTypes);
            anomalyTypeDetails.Add(BuildStructuredIssueDetail(reportableRowTypes));

            if (!config.DetailOutputEnabled)
            {
                var compactType = BuildCompactIssueType(reportableRowTypes);
                detailPreview.Add(BuildDetailPreview(
                    fileName,
                    relativePath,
                    location,
                    compactType,
                    BuildCompactIssueDetail(reportableRowTypes),
                    SeverityForIssueType(compactType),
                    BuildCompactIssueValue(reportableRowTypes, rowAnalyses[rowIndex]),
                    rowAnalyses[rowIndex].Time,
                    rowAnalyses[rowIndex].ReportValues));
                anomalyRows++;
                continue;
            }

            var detailTypes = BuildDetailPreviewIssueTypes(reportableRowTypes, rowAnalyses[rowIndex], config);

            foreach (var type in detailTypes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                detailPreview.Add(BuildDetailPreview(
                    fileName,
                    relativePath,
                    location,
                    type,
                    BuildRowIssueDetail(type, rowAnalyses[rowIndex], config),
                    SeverityForIssueType(type),
                    BuildIssueValue(type, rowAnalyses[rowIndex], config),
                    rowAnalyses[rowIndex].Time,
                    rowAnalyses[rowIndex].ReportValues));
            }

            anomalyRows++;
        }

        var offlineColumns = offlineInvalidCounts
            .Where(static pair => pair.Value > 0)
            .Select(static pair => pair.Key)
            .OrderBy(static column => Array.IndexOf(ReportValueColumns, column))
            .ThenBy(static column => column, StringComparer.Ordinal)
            .ToList();
        var offlineRatio = dataRows > 0 && keyColumnGroups.Count > 0
            ? (double)offlineInvalidCounts.Values.Sum() / (dataRows * keyColumnGroups.Count)
            : 0.0;
        var isOffline = offlineRatio > 0.5;
        var sensorFaults = sensorStates
            .Where(static pair => IsTemperatureColumn(pair.Key) && pair.Value.FaultCount > 0)
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new NativeSensorFault(pair.Key, pair.Value.FaultCount))
            .ToList();
        var sensorMissing = sensorStates
            .Where(static pair => pair.Value.HasObservedCell && !pair.Value.HasNonZeroValidValue)
            .Select(static pair => pair.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
        var sensorStatus = new NativeSensorStatusRow(
            fileName,
            location.Building,
            relativePath,
            location.Transformer,
            isOffline,
            sensorFaults.Select(static fault => fault.Name).ToList(),
            sensorMissing,
            isOffline ? "离线" : sensorFaults.Count > 0 || sensorMissing.Count > 0 ? "异常" : "正常",
            BuildSensorStatusReason(isOffline, sensorFaults, sensorMissing));

        if (isOffline)
        {
            var offlineDetail = offlineColumns.Count > 0
                ? string.Join(",", offlineColumns)
                : "所有电气参数";
            var offlineIssueType = $"设备离线({offlineDetail})";
            detailPreview.Add(BuildDetailPreview(
                fileName,
                relativePath,
                location,
                offlineIssueType,
                offlineIssueType,
                "高",
                "",
                ExtractDateFromFileName(fileName)));
            foreach (var fault in sensorFaults)
            {
                var sensorFaultIssueType = BuildSensorFaultIssueType(fault);
                detailPreview.Add(BuildDetailPreview(
                    fileName,
                    relativePath,
                    location,
                    sensorFaultIssueType,
                    sensorFaultIssueType,
                    "中",
                    "",
                    ExtractDateFromFileName(fileName)));
            }

            return NativeFileAnalysis.Anomaly(
                fileName,
                1 + sensorFaults.Count,
                "设备离线",
                sensorMissing,
                sensorStatus,
                detailPreview,
                BuildParityTrace(
                    parityTraceKey,
                    dataRows,
                    rawRowLabels,
                    reportableRowLabels,
                    anomalyTypeDetails,
                    "设备离线"));
        }

        if (sensorFaults.Count > 0)
        {
            anomalyRows += sensorFaults.Count;
        }

        foreach (var fault in sensorFaults)
        {
            var sensorFaultIssueType = BuildSensorFaultIssueType(fault);
            detailPreview.Add(BuildDetailPreview(
                fileName,
                relativePath,
                location,
                sensorFaultIssueType,
                sensorFaultIssueType,
                "中",
                "",
                ExtractDateFromFileName(fileName)));
        }

        var anomalyTypes = BuildFileAnomalyTypes(anomalyTypeDetails);
        if (sensorFaults.Count > 0)
        {
            var sensorTypes = string.Join("; ", sensorFaults.Select(BuildSensorFaultSummaryType));
            anomalyTypes = string.IsNullOrWhiteSpace(anomalyTypes)
                ? sensorTypes
                : $"{anomalyTypes}; {sensorTypes}";
        }

        var parityTrace = BuildParityTrace(
            parityTraceKey,
            dataRows,
            rawRowLabels,
            reportableRowLabels,
            anomalyTypeDetails,
            anomalyTypes);

        return anomalyRows > 0
            ? NativeFileAnalysis.Anomaly(
                fileName,
                anomalyRows,
                anomalyTypes,
                sensorMissing,
                sensorStatus,
                detailPreview,
                parityTrace)
            : NativeFileAnalysis.Normal(fileName, sensorMissing, sensorStatus, parityTrace);
    }

}
