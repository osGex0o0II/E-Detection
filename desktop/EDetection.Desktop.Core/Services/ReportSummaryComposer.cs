using System.Globalization;
using System.Text.RegularExpressions;
using EDetection.Desktop.Models;
using EDetection.NativeCore.Models;

namespace EDetection.Desktop.Services;

/// <summary>
/// Builds the human-readable report summary shown in the UI and copied to the
/// clipboard.  The composer is stateless; each call works on a snapshot so it
/// can be used from tests without a WinUI message loop.
/// </summary>
public sealed class ReportSummaryComposer
{
    public string BuildCompletionSummaryText(ReportSummaryContext context) =>
        new Computation(context).BuildCompletionSummaryText();

    private sealed class Computation
    {
        private readonly ReportSummaryContext _context;

        public Computation(ReportSummaryContext context) => _context = context;

        private string InputDirectory => _context.InputDirectory;

        private IReadOnlyList<ReportDetailPreview> DetailPreview => _context.DetailPreview;

        private IReadOnlyList<ReportDeviceSummary> HighRiskDevices => _context.HighRiskDevices;

        private IReadOnlyList<ReportIssueType> TopIssueTypes => _context.TopIssueTypes;

        private int TotalFiles => _context.TotalFiles;

        private int AnomalyFiles => _context.AnomalyFiles;

        private int AnomalyRecords => _context.AnomalyRecords;

        private int SkippedFiles => _context.SkippedFiles;

        private int ReportDeviceCount => _context.ReportDeviceCount;

        private int SensorOfflineDevices => _context.SensorOfflineDevices;

        private int SensorFaultRows => _context.SensorFaultRows;

        private int SensorMissingRows => _context.SensorMissingRows;

        private int SensorSkippedRows => _context.SensorSkippedRows;

        private string ProcessedSummary => _context.ProcessedSummary;

        private string RunElapsedText => _context.RunElapsedText;

        private string ReportPath => _context.ReportPath;

        public string BuildCompletionSummaryText()
        {
            var lines = new List<string>
            {
                BuildDetectionReportTitleText(),
                "",
            };

            var details = DetailPreview.ToList();
            lines.Add(BuildPlainConclusionText(details));
            lines.Add("");
            lines.AddRange(BuildDetectionReportParagraphs(details));
            lines.Add("");
            lines.Add(BuildDetectionOverviewText());

            return string.Join(Environment.NewLine, lines);
        }

        private string BuildDetectionReportTitleText()
        {
            var period = BuildDetectionPeriodText();
            return string.IsNullOrWhiteSpace(period)
                ? "E-Detection 检测汇报"
                : $"E-Detection 检测汇报（{period}）";
        }

        private string BuildDetectionPeriodText()
        {
            var dates = DetailPreview
                .Select(static detail => detail.Date)
                .Select(TryNormalizeDateText)
                .Where(static date => !string.IsNullOrWhiteSpace(date))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static date => date, StringComparer.Ordinal)
                .ToList();

            if (dates.Count == 0)
            {
                var inputDate = TryFindDateFromPath(InputDirectory);
                if (!string.IsNullOrWhiteSpace(inputDate))
                {
                    dates.Add(inputDate);
                }
            }

            var dateText = dates.Count switch
            {
                0 => "",
                1 => dates[0],
                _ => $"{dates.First()}至{dates.Last()}",
            };

            var hourText = dates.Count > 1
                ? ""
                : BuildDetectionHourRangeText(preferFullDay: dates.Count == 1 && TotalFiles > 0);
            if (string.IsNullOrWhiteSpace(dateText))
            {
                return hourText;
            }

            return string.IsNullOrWhiteSpace(hourText)
                ? dateText
                : $"{dateText}，{hourText}";
        }

        private string BuildDetectionHourRangeText(bool preferFullDay = false)
        {
            if (preferFullDay)
            {
                return "0-23时";
            }

            var hours = DetailPreview
                .Select(static detail => detail.Time)
                .Select(TryParseHour)
                .Where(static hour => hour >= 0)
                .Distinct()
                .OrderBy(static hour => hour)
                .ToList();

            if (hours.Count > 0)
            {
                return $"{hours.First()}-{hours.Last()}时";
            }

            return TotalFiles > 0 ? "0-23时" : "";
        }

        private string BuildPlainConclusionText(IReadOnlyList<ReportDetailPreview> details)
        {
            if (AnomalyRecords <= 0)
            {
                return "简要结论：本次未见明显电气故障数据。";
            }

            var offlineDetails = details.Where(IsOfflineIssue).ToList();
            var voltageDetails = details.Where(IsVoltageOrPtIssue).ToList();
            var ctDetails = details.Where(IsCtIssue).ToList();
            var sensorFaultDetails = details.Where(IsSensorFaultIssue).ToList();
            var temperatureDetails = details.Where(detail => IsTemperatureIssue(detail) && !IsSensorFaultIssue(detail)).ToList();
            var currentDetails = details.Where(IsCurrentIssue).ToList();
            var constantDetails = details
                .Where(IsConstantIssue)
                .Where(detail => !IsOfflineIssue(detail)
                                 && !IsVoltageOrPtIssue(detail)
                                 && !IsCtIssue(detail)
                                 && !IsSensorFaultIssue(detail)
                                 && !IsTemperatureIssue(detail)
                                 && !IsCurrentIssue(detail))
                .ToList();
            var parts = new List<string>();

            if (HasIssueEvidence(offlineDetails, IsOfflineIssueText))
            {
                parts.Add($"{BuildDeviceScopeTextForIssue(offlineDetails, IsOfflineIssueText)}通信/采集疑似中断");
            }

            if (HasIssueEvidence(voltageDetails, IsVoltageOrPtIssueText))
            {
                parts.Add($"{BuildDeviceScopeTextForIssue(voltageDetails, IsVoltageOrPtIssueText)}PT/电压采样异常");
            }

            if (HasIssueEvidence(ctDetails, IsCtIssueText))
            {
                parts.Add($"{BuildDeviceScopeTextForIssue(ctDetails, IsCtIssueText)}CT极性或相序疑似异常");
            }

            if (HasIssueEvidence(sensorFaultDetails, IsSensorFaultIssueText))
            {
                parts.Add($"{BuildDeviceScopeTextForIssue(sensorFaultDetails, IsSensorFaultIssueText)}温度传感器读数明显不合理");
            }

            if (HasIssueEvidence(temperatureDetails, IsTemperatureRunIssueText))
            {
                parts.Add($"{BuildDeviceScopeTextForIssue(temperatureDetails, IsTemperatureRunIssueText)}温度运行值需复核");
            }

            if (HasIssueEvidence(currentDetails, IsCurrentIssueText))
            {
                parts.Add($"{BuildDeviceScopeTextForIssue(currentDetails, IsCurrentIssueText)}电流偏大");
            }

            if (HasIssueEvidence(constantDetails, IsConstantIssueText))
            {
                parts.Add($"{BuildDeviceScopeTextForIssue(constantDetails, IsConstantIssueText)}数据恒定/冻结");
            }

            return parts.Count > 0
                ? $"简要结论：本次主要发现{string.Join("；", parts.Take(4))}。"
                : $"简要结论：本次发现 {AnomalyRecords} 条异常记录，建议优先查看高风险设备。";
        }

        private IReadOnlyList<string> BuildDetectionReportParagraphs(IReadOnlyList<ReportDetailPreview> details)
        {
            if (AnomalyRecords <= 0)
            {
                return new[] { BuildNoAnomalyReportText() };
            }

            var paragraphs = new List<string>();
            var offlineDetails = details.Where(IsOfflineIssue).ToList();
            var voltageDetails = details.Where(IsVoltageOrPtIssue).ToList();
            var ctDetails = details.Where(IsCtIssue).ToList();
            var sensorFaultDetails = details.Where(IsSensorFaultIssue).ToList();
            var temperatureDetails = details.Where(detail => IsTemperatureIssue(detail) && !IsSensorFaultIssue(detail)).ToList();
            var currentDetails = details.Where(IsCurrentIssue).ToList();
            var constantDetails = details
                .Where(IsConstantIssue)
                .Where(detail => !IsOfflineIssue(detail)
                                 && !IsVoltageOrPtIssue(detail)
                                 && !IsCtIssue(detail)
                                 && !IsSensorFaultIssue(detail)
                                 && !IsTemperatureIssue(detail)
                                 && !IsCurrentIssue(detail))
                .ToList();
            var describedDetails = offlineDetails
                .Concat(voltageDetails)
                .Concat(ctDetails)
                .Concat(sensorFaultDetails)
                .Concat(temperatureDetails)
                .Concat(currentDetails)
                .Concat(constantDetails)
                .Distinct()
                .ToList();
            var otherDetails = details.Except(describedDetails).ToList();
            var timeScope = BuildDetectionTimeScopeWord();

            if (HasIssueEvidence(offlineDetails, IsOfflineIssueText))
            {
                paragraphs.Add(
                    $"{BuildDeviceScopeTextForIssue(offlineDetails, IsOfflineIssueText)}{timeScope}通信/采集疑似中断，电压、电流等关键电气量无法有效读取。优先核查设备供电、通信链路、采集网关和点位接入状态。");
            }

            if (HasIssueEvidence(voltageDetails, IsVoltageOrPtIssueText))
            {
                paragraphs.Add(
                    $"{BuildDeviceScopeTextForIssue(voltageDetails, IsVoltageOrPtIssueText)}{timeScope}电压采样异常，可能伴随数据恒定。优先核查 PT 二次接线、采样通道、变比配置和采集模块，再判断是否存在真实过压或欠压。");
            }

            if (HasIssueEvidence(ctDetails, IsCtIssueText))
            {
                paragraphs.Add(
                    $"{BuildDeviceScopeTextForIssue(ctDetails, IsCtIssueText)}CT 极性或相序疑似异常，表现为电流方向、功率方向或相序关系不一致。建议由电气或自控人员核对 CT 二次接线、相序、倍率和点位配置。");
            }

            if (HasIssueEvidence(sensorFaultDetails, IsSensorFaultIssueText))
            {
                paragraphs.Add(
                    $"{BuildDeviceScopeTextForIssue(sensorFaultDetails, IsSensorFaultIssueText)}出现明显不合理的传感器读数{BuildRepresentativeValueText(sensorFaultDetails)}，不宜直接按真实温度判断。优先核查温度探头、接线和采集通道。");
            }

            if (HasIssueEvidence(temperatureDetails, IsTemperatureRunIssueText))
            {
                paragraphs.Add(
                    $"{BuildDeviceScopeTextForIssue(temperatureDetails, IsTemperatureRunIssueText)}{timeScope}温度运行值异常。建议结合现场负载、散热风扇、柜内通风和探头安装位置复核，再判断是否为设备发热。");
            }

            if (HasIssueEvidence(currentDetails, IsCurrentIssueText))
            {
                paragraphs.Add(
                    $"{BuildDeviceScopeTextForIssue(currentDetails, IsCurrentIssueText)}{timeScope}电流偏大。可能为实际负荷偏高，也可能是互感器倍率或采集点配置不对，建议结合现场负载和额定容量复核。");
            }

            if (HasIssueEvidence(constantDetails, IsConstantIssueText))
            {
                paragraphs.Add(
                    $"{BuildDeviceScopeTextForIssue(constantDetails, IsConstantIssueText)}{timeScope}数据长时间恒定，疑似采集冻结或历史数据未刷新。建议先确认设备是否停运，再检查通信、采集服务和历史库写入。");
            }

            if (otherDetails.Count > 0)
            {
                paragraphs.Add(
                    $"{BuildDeviceScopeText(otherDetails)}还有{BuildIssueTypeListText(otherDetails)}等问题，先按明细逐项核对现场状态。");
            }

            var sensorParagraph = BuildSensorStatusReportText();
            if (!string.IsNullOrWhiteSpace(sensorParagraph))
            {
                paragraphs.Add(sensorParagraph);
            }

            return paragraphs.Count > 0 ? paragraphs : [BuildNoAnomalyReportText()];
        }

        private string BuildDetectionOverviewText()
        {
            var reportText = string.IsNullOrWhiteSpace(ReportPath)
                ? "报告未生成"
                : $"报告已生成：{Path.GetFileName(ReportPath)}";
            var skippedText = SkippedFiles > 0 ? $"，跳过 {SkippedFiles} 份" : "";

            return $"检测概况：完成 {ProcessedSummary} 份文件，异常文件 {AnomalyFiles} 个，异常记录 {AnomalyRecords} 条，涉及设备 {ReportDeviceCount} 台{skippedText}，耗时 {RunElapsedText}，{reportText}。";
        }

        private string BuildNoAnomalyReportText()
        {
            var sensorText = BuildSensorStatusReportText();
            if (!string.IsNullOrWhiteSpace(sensorText))
            {
                return $"本时段未见明显电气异常记录。{sensorText}";
            }

            return "本时段未见明显电气异常记录，数据整体正常。";
        }

        private string BuildSensorStatusReportText()
        {
            var parts = new List<string>();
            if (SensorOfflineDevices > 0)
            {
                parts.Add($"离线记录 {SensorOfflineDevices} 条");
            }

            if (SensorFaultRows > 0)
            {
                parts.Add($"故障 {SensorFaultRows} 条");
            }

            if (SensorMissingRows > 0)
            {
                parts.Add($"未配置 {SensorMissingRows} 条");
            }

            if (SensorSkippedRows > 0 && SensorSkippedRows != SkippedFiles)
            {
                parts.Add($"跳过 {SensorSkippedRows} 条");
            }

            return parts.Count > 0
                ? $"传感器状态存在{string.Join("、", parts)}，建议先补齐点位配置，再处理离线和故障测点。"
                : "";
        }

        private string BuildDetectionTimeScopeWord() =>
            TotalFiles > 0 || string.Equals(BuildDetectionHourRangeText(), "0-23时", StringComparison.Ordinal)
                ? "全天"
                : "本时段内";

        private string BuildDeviceScopeText(IReadOnlyList<ReportDetailPreview> details)
        {
            var deviceGroups = details
                .Select(static detail => new
                {
                    Building = string.IsNullOrWhiteSpace(detail.Building) ? "未知区域" : detail.Building.Trim(),
                    Transformer = string.IsNullOrWhiteSpace(detail.Transformer) ? "未知设备" : detail.Transformer.Trim(),
                })
                .Distinct()
                .GroupBy(static item => item.Building, static item => item.Transformer)
                .OrderByDescending(static group => group.Count())
                .ThenBy(static group => group.Key, StringComparer.CurrentCulture)
                .ToList();
            var deviceCount = deviceGroups.Sum(static group => group.Distinct(StringComparer.CurrentCulture).Count());

            if (deviceCount == 0)
            {
                return $"{details.Count} 条记录";
            }

            var groupTexts = deviceGroups
                .Take(4)
                .Select(static group =>
                {
                    var transformers = group
                        .Distinct(StringComparer.CurrentCulture)
                        .OrderBy(static transformer => transformer, StringComparer.CurrentCulture)
                        .Take(6)
                        .ToList();
                    var suffix = group.Distinct(StringComparer.CurrentCulture).Count() > transformers.Count ? "等" : "";
                    return $"{group.Key}（{string.Join("、", transformers)}{suffix}）";
                })
                .ToList();
            var moreText = deviceGroups.Count > groupTexts.Count ? "等" : "";

            return $"{string.Join("、", groupTexts)}{moreText}共 {deviceCount} 台设备";
        }

        private bool HasIssueEvidence(IReadOnlyList<ReportDetailPreview> details, Func<string, bool> issueTextMatches) =>
            details.Count > 0
            || HighRiskDevices.Any(device => issueTextMatches(device.MainIssueTypes))
            || TopIssueTypes.Any(issue => issueTextMatches(issue.Name));

        private string BuildDeviceScopeTextForIssue(
            IReadOnlyList<ReportDetailPreview> details,
            Func<string, bool> issueTextMatches)
        {
            if (details.Count > 0)
            {
                return BuildDeviceScopeText(details);
            }

            var fallbackDetails = HighRiskDevices
                .Where(device => issueTextMatches(device.MainIssueTypes))
                .Select(static device => new ReportDetailPreview
                {
                    Building = device.Building,
                    Transformer = device.Transformer,
                })
                .Take(8)
                .ToList();

            return fallbackDetails.Count > 0 ? BuildDeviceScopeText(fallbackDetails) : "相关设备";
        }

        private string BuildIssueTypeListText(IReadOnlyList<ReportDetailPreview> details)
        {
            var issueTypes = details
                .SelectMany(static detail => SplitIssueTypes(detail.IssueType))
                .Where(static issue => !string.IsNullOrWhiteSpace(issue))
                .Select(static issue => issue.Trim())
                .Where(static issue => !issue.StartsWith("+", StringComparison.Ordinal))
                .Distinct(StringComparer.CurrentCulture)
                .Take(3)
                .ToList();

            return issueTypes.Count > 0
                ? string.Join("、", issueTypes)
                : "未归类异常";
        }

        private string BuildRepresentativeValueText(IReadOnlyList<ReportDetailPreview> details)
        {
            var directValues = details
                .Select(static detail => detail.IssueValue)
                .Where(static value => !string.IsNullOrWhiteSpace(value) && value.Trim() != "-")
                .Select(static value => value!.Trim());
            var detailValues = details
                .SelectMany(static detail => ExtractRepresentativeValues(detail.IssueDetail));
            var values = directValues
                .Concat(detailValues)
                .Distinct(StringComparer.CurrentCulture)
                .Take(3)
                .ToList();

            return values.Count > 0 ? $"（代表值：{string.Join("；", values)}）" : "";
        }

        private static bool IsOfflineIssue(ReportDetailPreview detail) =>
            IsOfflineIssueText(BuildIssueSearchText(detail));

        private static bool IsVoltageOrPtIssue(ReportDetailPreview detail) =>
            IsVoltageOrPtIssueText(BuildIssueSearchText(detail));

        private static bool IsCtIssue(ReportDetailPreview detail) =>
            IsCtIssueText(BuildIssueSearchText(detail));

        private static bool IsTemperatureIssue(ReportDetailPreview detail) =>
            IsTemperatureIssueText(BuildIssueSearchText(detail));

        private static bool IsSensorFaultIssue(ReportDetailPreview detail) =>
            IsSensorFaultIssueText(BuildIssueSearchText(detail));

        private static bool IsCurrentIssue(ReportDetailPreview detail) =>
            IsCurrentIssueText(BuildIssueSearchText(detail));

        private static bool IsConstantIssue(ReportDetailPreview detail) =>
            IsConstantIssueText(BuildIssueSearchText(detail));

        private static bool IsOfflineIssueText(string text) =>
            ContainsAny(text, "设备离线", "通信中断", "通讯中断", "-1.0", "-1");

        private static bool IsVoltageOrPtIssueText(string text) =>
            ContainsAny(text, "电压", "PT", "电压互感器");

        private static bool IsCtIssueText(string text) =>
            ContainsAny(text, "CT极性", "CT", "功率方向", "接线相序");

        private static bool IsTemperatureIssueText(string text) =>
            ContainsAny(text, "温度");

        private static bool IsTemperatureRunIssueText(string text) =>
            IsTemperatureIssueText(text) && !IsSensorFaultIssueText(text);

        private static bool IsSensorFaultIssueText(string text) =>
            ContainsAny(text, "传感器故障", "传感器失效", "2867.2");

        private static bool IsCurrentIssueText(string text) =>
            ContainsAny(text, "电流过大", "电流过载", "电流偏大");

        private static bool IsConstantIssueText(string text) =>
            ContainsAny(text, "数据恒定", "数据冻结", "恒定不变");

        private static string BuildIssueSearchText(ReportDetailPreview detail) =>
            string.Join(" ", detail.IssueType, detail.IssueDetail, detail.IssueValue, detail.RecommendedAction);

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(needle => text.Contains(needle, StringComparison.CurrentCultureIgnoreCase));

        private static IEnumerable<string> SplitIssueTypes(string? issueTypes) =>
            (issueTypes ?? "")
                .Split(['；', ';', '、', ',', '，', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static IEnumerable<string> ExtractRepresentativeValues(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                yield break;
            }

            foreach (Match match in Regex.Matches(text, @"-?\d+(?:\.\d+)?\s*(?:°C|℃|kV|V|A|kW|kvar)|-1\.0|0\.0"))
            {
                var value = match.Value.Trim();
                if (value.Length > 0 && value.Any(char.IsDigit))
                {
                    yield return value;
                }
            }
        }

        private static string TryNormalizeDateText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            var text = value.Trim();
            string[] formats = ["yyyy-MM-dd", "yyyy/M/d", "yyyy.MM.dd", "yyyyMMdd", "yyyy年M月d日"];
            if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactDate)
                || DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out exactDate))
            {
                return exactDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            return "";
        }

        private static string TryFindDateFromPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            var match = Regex.Match(path, @"(?<year>\d{4})[-_/年.]?(?<month>\d{1,2})[-_/月.]?(?<day>\d{1,2})");
            if (!match.Success)
            {
                return "";
            }

            return int.TryParse(match.Groups["year"].Value, out var year)
                && int.TryParse(match.Groups["month"].Value, out var month)
                && int.TryParse(match.Groups["day"].Value, out var day)
                && year >= 2000
                && month is >= 1 and <= 12
                && day is >= 1 and <= 31
                ? $"{year:0000}-{month:00}-{day:00}"
                : "";
        }

        private static int TryParseHour(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return -1;
            }

            if (DateTime.TryParse(value.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out var dateTime))
            {
                return dateTime.Hour;
            }

            var match = Regex.Match(value, @"^\s*(?<hour>\d{1,2})");
            return match.Success
                && int.TryParse(match.Groups["hour"].Value, out var hour)
                && hour is >= 0 and <= 23
                    ? hour
                    : -1;
        }
    }
}
