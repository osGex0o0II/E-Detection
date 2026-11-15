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
    public async Task<int> RunDetectionAsync(
        DetectionRequest request,
        IProgress<DetectionBackendEvent> progress,
        CancellationToken cancellationToken)
    {
        await Task.Yield();

        if (string.IsNullOrWhiteSpace(request.InputDirectory))
        {
            progress.Report(new DetectionBackendEvent
            {
                EventName = "error",
                ErrorType = nameof(ArgumentException),
                Message = "输入目录不能为空。",
            });
            return 1;
        }

        if (!Directory.Exists(request.InputDirectory))
        {
            progress.Report(new DetectionBackendEvent
            {
                EventName = "error",
                ErrorType = nameof(DirectoryNotFoundException),
                Message = $"输入目录不存在: {request.InputDirectory}",
            });
            return 1;
        }

        var inputRoot = Path.GetFullPath(request.InputDirectory);
        var outputRoot = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? inputRoot
            : Path.GetFullPath(request.OutputDirectory);
        var configResult = LoadConfig(request.ConfigPath);
        if (!configResult.IsSuccess)
        {
            progress.Report(new DetectionBackendEvent
            {
                EventName = "error",
                ErrorType = configResult.ErrorType,
                Message = configResult.ErrorMessage,
            });
            return 1;
        }

        var config = configResult.Config;
        var parityTraceKey = GetParityTraceKey();
        var files = EnumerateCsvFiles(inputRoot, cancellationToken);
        var generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var startedAt = DateTimeOffset.UtcNow;
        var normalFiles = 0;
        var skippedFiles = 0;
        var anomalyFiles = 0;
        var anomalyRecords = 0;
        var sensorStatusRows = new List<NativeSensorStatusRow>();
        var skippedStatusRows = new List<NativeSensorStatusRow>();
        var skippedDetails = 0;
        var detailPreview = new List<ReportDetailPreview>();
        IReadOnlyList<ReportDetailPreview> reportDetails = [];
        DetectionBackendEvent? reportSummary = null;
        string? reportPath = null;

        progress.Report(new DetectionBackendEvent
        {
            EventName = "run_started",
            InputDirectory = inputRoot,
            OutputDirectory = outputRoot,
            TotalFiles = files.Count,
            WriteReport = request.WriteReport,
            GeneratedAt = generatedAt,
        });

        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = files[index];
            var fileName = Path.GetFileName(path);
            var relativePath = Path.GetRelativePath(inputRoot, path);
            var location = ExtractLocation(path, inputRoot);
            var isHighVoltage = fileName.Contains("高压", StringComparison.OrdinalIgnoreCase);
            var analysis = isHighVoltage
                ? NativeFileAnalysis.Skipped($"跳过高压设备: {fileName}")
                : AnalyzeFile(path, fileName, relativePath, location, config, parityTraceKey, cancellationToken);
            if (parityTraceKey is not null && analysis.ParityTrace is null)
            {
                analysis = analysis with
                {
                    ParityTrace = BuildUnavailableParityTrace(parityTraceKey),
                };
            }

            if (isHighVoltage)
            {
                skippedFiles++;
                skippedDetails++;
                skippedStatusRows.Add(NativeSensorStatusRow.Skipped(fileName, relativePath, location, analysis.Message));
            }
            else if (analysis.Status == "skipped")
            {
                skippedFiles++;
                skippedDetails++;
                skippedStatusRows.Add(NativeSensorStatusRow.Skipped(fileName, relativePath, location, analysis.Message));
            }
            else if (analysis.AnomalyCount > 0)
            {
                anomalyFiles++;
                anomalyRecords += analysis.AnomalyCount;
                sensorStatusRows.Add(analysis.SensorStatus);
                AddDetailPreview(detailPreview, analysis.DetailPreview, relativePath);
            }
            else
            {
                normalFiles++;
                sensorStatusRows.Add(analysis.SensorStatus);
                AddDetailPreview(detailPreview, analysis.DetailPreview, relativePath);
            }

            progress.Report(new DetectionBackendEvent
            {
                EventName = "file_result",
                SourceFile = fileName,
                RelativePath = relativePath,
                Building = location.Building,
                Transformer = location.Transformer,
                Status = analysis.Status,
                Message = analysis.Message,
                ProcessedFiles = index + 1,
                TotalFiles = files.Count,
                AnomalyCount = analysis.AnomalyCount,
                AnomalyTypes = analysis.AnomalyTypes,
                NativeParityTrace = analysis.ParityTrace,
            });
            progress.Report(new DetectionBackendEvent
            {
                EventName = "file_progress",
                SourceFile = fileName,
                ProcessedFiles = index + 1,
                TotalFiles = files.Count,
                Percent = files.Count == 0 ? 1.0 : (double)(index + 1) / files.Count,
            });
        }

        if (anomalyRecords > 0 || sensorStatusRows.Count > 0 || skippedDetails > 0)
        {
            var sortedDetailPreview = detailPreview
                .OrderBy(detail =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return SeverityRank(detail.Severity);
                })
                .ToList();
            reportDetails = sortedDetailPreview;
            cancellationToken.ThrowIfCancellationRequested();
            var deviceSummaries = BuildDeviceSummaries(sortedDetailPreview, cancellationToken);
            reportSummary = new DetectionBackendEvent
            {
                EventName = "report_summary",
                InputDirectory = inputRoot,
                OutputDirectory = outputRoot,
                TotalFiles = files.Count,
                ProcessedFiles = files.Count,
                NormalFiles = normalFiles,
                AnomalyFiles = anomalyFiles,
                AnomalyRecords = anomalyRecords,
                SkippedFiles = skippedFiles,
                GeneratedAt = generatedAt,
                DeviceCount = deviceSummaries.Count,
                HighRiskDevices = deviceSummaries
                    .Where(static device => device.HighestSeverity == "高")
                    .Take(10)
                    .ToList(),
                TopIssueTypes = BuildTopIssueTypes(sortedDetailPreview),
                SensorOverview = new ReportSensorOverview
                {
                    TotalRows = sensorStatusRows.Count + skippedDetails,
                    OfflineDevices = sensorStatusRows.Count(static row => row.IsOffline),
                    SensorFaultRows = sensorStatusRows.Count(static row => row.SensorFaults.Count > 0),
                    SensorMissingRows = sensorStatusRows.Count(static row => row.SensorMissing.Count > 0),
                    SkippedRows = skippedDetails,
                },
                DetailPreviewCount = sortedDetailPreview.Count,
                DetailPreview = sortedDetailPreview.Take(100).ToList(),
            };
            progress.Report(reportSummary);
        }

        if (request.WriteReport && reportSummary is not null)
        {
            try
            {
                reportPath = WriteNativeExcelReport(
                    outputRoot,
                    generatedAt,
                    reportSummary,
                    config,
                    reportDetails,
                    sensorStatusRows.Concat(skippedStatusRows).ToList(),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or InvalidDataException
                                       or NotSupportedException
                                       or ArgumentException)
            {
                progress.Report(new DetectionBackendEvent
                {
                    EventName = "error",
                    ErrorType = ex.GetType().Name,
                    Message = $"报告写入失败: {outputRoot} ({ex.Message})",
                });
                return 1;
            }

            progress.Report(new DetectionBackendEvent
            {
                EventName = "report_written",
                ReportPath = reportPath,
                AnomalyRecords = anomalyRecords,
            });
        }

        progress.Report(new DetectionBackendEvent
        {
            EventName = "run_completed",
            InputDirectory = inputRoot,
            OutputDirectory = outputRoot,
            TotalFiles = files.Count,
            ProcessedFiles = files.Count,
            NormalFiles = normalFiles,
            AnomalyFiles = anomalyFiles,
            AnomalyRecords = anomalyRecords,
            SkippedFiles = skippedFiles,
            ReportPath = reportPath,
            DurationSeconds = (DateTimeOffset.UtcNow - startedAt).TotalSeconds,
            GeneratedAt = generatedAt,
        });
        return 0;
    }

}
