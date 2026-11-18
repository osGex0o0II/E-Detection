using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EDetection.NativeCore.Models;

public sealed class ReportDetailPreview
{
    [JsonPropertyName("building")]
    public string? Building { get; set; }

    [JsonPropertyName("transformer")]
    public string? Transformer { get; set; }

    [JsonPropertyName("relative_path")]
    public string? RelativePath { get; set; }

    [JsonPropertyName("source_file")]
    public string? SourceFile { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("time")]
    [JsonConverter(typeof(FlexibleStringJsonConverter))]
    public string? Time { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("issue_type")]
    public string? IssueType { get; set; }

    [JsonPropertyName("issue_detail")]
    public string? IssueDetail { get; set; }

    [JsonPropertyName("issue_value")]
    public string? IssueValue { get; set; }

    [JsonPropertyName("recommended_action")]
    public string? RecommendedAction { get; set; }

    [JsonIgnore]
    public Dictionary<string, double> ReportValues { get; set; } = [];

    [JsonIgnore]
    public string LocationText
    {
        get
        {
            var parts = new[] { Building, Transformer }
                .Where(value => !string.IsNullOrWhiteSpace(value));
            var location = string.Join(" / ", parts);
            return string.IsNullOrWhiteSpace(location) ? SourceFile ?? "" : location;
        }
    }

    [JsonIgnore]
    public string TimeText
    {
        get
        {
            // Native rows store the hour cell verbatim ("7"), and files without
            // a date in the name carry the literal "日期未知".  Printing both
            // produced "日期未知 7时" in every row; normalize to "07:00".
            var date = NormalizeDateText(Date);
            var time = NormalizeTimeText(Time);
            var text = string.Join(" ", new[] { date, time }.Where(value => !string.IsNullOrWhiteSpace(value)));
            return string.IsNullOrWhiteSpace(text) ? SourceFile ?? "" : text;
        }
    }

    private static string NormalizeDateText(string? value)
    {
        var text = value?.Trim() ?? "";
        return text is "" or "日期未知" or "-" ? "" : text;
    }

    private static string NormalizeTimeText(string? value)
    {
        var text = value?.Trim() ?? "";
        if (text is "" or "-")
        {
            return "";
        }

        if (text.All(char.IsAsciiDigit) && text.Length <= 2 && int.TryParse(text, out var hour) && hour is >= 0 and <= 23)
        {
            return $"{hour:00}:00";
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            text,
            @"^(?<hour>\d{1,2}):(?<minute>\d{2})(?::\d{2})?$");
        if (match.Success
            && int.TryParse(match.Groups["hour"].Value, out var parsedHour)
            && parsedHour is >= 0 and <= 23)
        {
            return $"{parsedHour:00}:{match.Groups["minute"].Value}";
        }

        return text;
    }

    [JsonIgnore]
    public string IssueText
    {
        get
        {
            var type = IssueType?.Trim() ?? "";
            var detail = IssueDetail?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(detail))
            {
                return type;
            }

            if (string.IsNullOrWhiteSpace(type))
            {
                return detail;
            }

            // With detail output disabled the native backend stores the same
            // compact label in both fields, which rendered as
            // "CT极性异常 · CT极性异常" in every row.  Collapse that instead of
            // printing the label twice.
            if (string.Equals(type, detail, StringComparison.Ordinal))
            {
                return type;
            }

            if (detail.StartsWith(type, StringComparison.Ordinal))
            {
                return detail;
            }

            return $"{type} · {detail}";
        }
    }

    [JsonIgnore]
    public string ValueText => string.IsNullOrWhiteSpace(IssueValue) ? "-" : IssueValue;

    public string ToClipboardText() =>
        string.Join(
            Environment.NewLine,
            $"等级: {Severity ?? ""}",
            $"设备: {LocationText}",
            $"时间: {TimeText}",
            $"异常: {IssueText}",
            $"异常值: {ValueText}",
            $"建议: {RecommendedAction ?? ""}",
            $"来源: {RelativePath ?? SourceFile ?? ""}");

    public bool Matches(string query, string severity, string issueType)
    {
        if (!string.IsNullOrWhiteSpace(severity)
            && !string.Equals(Severity, severity, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(issueType)
            && !string.Equals(IssueType, issueType, StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var haystack = string.Join(
            "\n",
            Building,
            Transformer,
            RelativePath,
            SourceFile,
            Date,
            Time,
            Severity,
            IssueType,
            IssueDetail,
            IssueValue,
            RecommendedAction);
        return haystack.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }
}

internal sealed class FlexibleStringJsonConverter : JsonConverter<string?>
{
    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number when reader.TryGetInt64(out var integer) => integer.ToString(CultureInfo.InvariantCulture),
            JsonTokenType.Number when reader.TryGetDouble(out var number) => number.ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => reader.GetString(),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        string? value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value);
    }
}
