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
    private static IReadOnlyList<string> ReadCsvLines(
        string path,
        CancellationToken cancellationToken)
    {
        foreach (var encoding in GetCandidateEncodings(path, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return ReadAllLines(path, encoding, cancellationToken);
            }
            catch (DecoderFallbackException)
            {
            }
        }

        return ReadAllLines(path, Encoding.UTF8, cancellationToken);
    }

    private static IReadOnlyList<string> ReadAllLines(
        string path,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        using var reader = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lines.Add(line);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return lines;
    }

    private static IEnumerable<Encoding> GetCandidateEncodings(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var encodings = new List<Encoding>();
        AddEncoding(encodings, DetectPreferredEncoding(path));
        foreach (var encoding in new[]
                 {
                     GetStrictEncoding(936),
                     new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true),
                     GetStrictEncoding(54936),
                     new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddEncoding(encodings, encoding);
        }

        return encodings;
    }

    private static Encoding DetectPreferredEncoding(string path)
    {
        var head = new byte[4096];
        var bytesRead = 0;
        try
        {
            using var stream = File.OpenRead(path);
            bytesRead = stream.Read(head, 0, head.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return GetStrictEncoding(54936);
        }

        if (bytesRead >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
        }

        return CanDecodeUtf8(head.AsSpan(0, bytesRead))
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            : GetStrictEncoding(54936);
    }

    private static bool CanDecodeUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static void AddEncoding(List<Encoding> encodings, Encoding encoding)
    {
        if (!encodings.Any(existing => existing.CodePage == encoding.CodePage))
        {
            encodings.Add(encoding);
        }
    }

    private static Encoding GetStrictEncoding(int codePage) =>
        Encoding.GetEncoding(
            codePage,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

    // A trailing annotation such as "(A)", "(°C)" or "（kW）" states the unit, not
    // the phase.  It is removed before any phase token is matched so the ampere
    // unit in "B相电流(A)" can never be read as evidence of phase A.
    private static string StripUnitAnnotation(string header)
    {
        var value = header.Trim();
        while (value.Length > 2)
        {
            var open = value[^1] switch
            {
                ')' => '(',
                ']' => '[',
                '）' => '（',
                '】' => '【',
                _ => '\0',
            };
            if (open == '\0')
            {
                return value;
            }

            var openIndex = value.LastIndexOf(open);
            if (openIndex <= 0)
            {
                // The whole header is one annotation - keep it rather than
                // reduce the column to an empty name.
                return value;
            }

            value = value[..openIndex].TrimEnd();
        }

        return value;
    }

    private static bool IsLatinAlphanumeric(char value) =>
        value is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9');

    // Matches <token> only where it is not glued to another latin letter or
    // digit, so "Ua" matches "Ua" and "Ua电压" but never "Uab"; and a bare "A"
    // is never treated as a phase marker on its own.
    private static bool ContainsLatinToken(string value, string token)
    {
        var start = 0;
        while (start <= value.Length - token.Length)
        {
            var index = value.IndexOf(token, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var after = index + token.Length;
            if ((index == 0 || !IsLatinAlphanumeric(value[index - 1]))
                && (after >= value.Length || !IsLatinAlphanumeric(value[after])))
            {
                return true;
            }

            start = index + 1;
        }

        return false;
    }

    private static string NormalizeColumnName(string header)
    {
        var normalized = StripUnitAnnotation(header);
        if (ContainsLatinToken(normalized, "Uab")
            || ContainsLatinToken(normalized, "Ua")
            || normalized.Contains("A相电压", StringComparison.Ordinal)
            || normalized.Contains("电压A相", StringComparison.Ordinal))
        {
            return "Uab";
        }

        if (ContainsLatinToken(normalized, "Ubc")
            || ContainsLatinToken(normalized, "Ub")
            || normalized.Contains("B相电压", StringComparison.Ordinal)
            || normalized.Contains("电压B相", StringComparison.Ordinal))
        {
            return "Ubc";
        }

        if (ContainsLatinToken(normalized, "Uca")
            || ContainsLatinToken(normalized, "Uc")
            || normalized.Contains("C相电压", StringComparison.Ordinal)
            || normalized.Contains("电压C相", StringComparison.Ordinal))
        {
            return "Uca";
        }

        if (ContainsLatinToken(normalized, "Ia")
            || normalized.Contains("A相电流", StringComparison.Ordinal)
            || normalized.Contains("电流A相", StringComparison.Ordinal))
        {
            return "Ia";
        }

        if (ContainsLatinToken(normalized, "Ib")
            || normalized.Contains("B相电流", StringComparison.Ordinal)
            || normalized.Contains("电流B相", StringComparison.Ordinal))
        {
            return "Ib";
        }

        if (ContainsLatinToken(normalized, "Ic")
            || normalized.Contains("C相电流", StringComparison.Ordinal)
            || normalized.Contains("电流C相", StringComparison.Ordinal))
        {
            return "Ic";
        }

        if (normalized.Contains("有功", StringComparison.Ordinal))
        {
            return "有功功率";
        }

        if (normalized.Contains("无功", StringComparison.Ordinal))
        {
            return "无功功率";
        }

        if (normalized.Contains("功率因数", StringComparison.Ordinal)
            || ContainsLatinToken(normalized, "PF"))
        {
            return "功率因数";
        }

        // A temperature column is rule-evaluated only when it names a phase.
        // Ambient, oil and cabinet temperatures are different physical
        // quantities with different limits, so they fall through to their own
        // name and are never averaged into a winding-temperature reading.
        if (ContainsLatinToken(normalized, "Ta")
            || normalized.Contains("A相温", StringComparison.Ordinal)
            || normalized.Contains("温度A相", StringComparison.Ordinal))
        {
            return "A相温度";
        }

        if (ContainsLatinToken(normalized, "Tb")
            || normalized.Contains("B相温", StringComparison.Ordinal)
            || normalized.Contains("温度B相", StringComparison.Ordinal))
        {
            return "B相温度";
        }

        if (ContainsLatinToken(normalized, "Tc")
            || normalized.Contains("C相温", StringComparison.Ordinal)
            || normalized.Contains("温度C相", StringComparison.Ordinal))
        {
            return "C相温度";
        }

        return normalized;
    }

    private static bool IsVoltageColumn(string normalizedHeader) =>
        normalizedHeader is "Uab" or "Ubc" or "Uca";

    private static bool IsCurrentColumn(string normalizedHeader) =>
        normalizedHeader is "Ia" or "Ib" or "Ic";

    private static bool IsTemperatureColumn(string normalizedHeader) =>
        normalizedHeader is "A相温度" or "B相温度" or "C相温度";

    private static bool IsKeyParameterColumn(string normalizedHeader) =>
        normalizedHeader is "Uab" or "Ubc" or "Uca" or "Ia" or "Ib" or "Ic";

    // Every column the rule engine reads.  Only these need an unambiguous
    // header-to-measurement mapping; free-text columns are carried through.
    private static bool IsRuleEvaluatedColumn(string normalizedHeader) =>
        IsKeyParameterColumn(normalizedHeader)
        || IsTemperatureColumn(normalizedHeader)
        || normalizedHeader is "有功功率" or "无功功率" or "功率因数";

    private static int FindTimeColumnIndex(IReadOnlyList<string> headers, out bool matchedTimeHeader)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            var header = headers[index];
            if (header.Contains("时间", StringComparison.Ordinal)
                || header.Contains("时刻", StringComparison.Ordinal)
                || header.Contains("Date", StringComparison.OrdinalIgnoreCase)
                || header.Contains("Time", StringComparison.OrdinalIgnoreCase))
            {
                matchedTimeHeader = true;
                return index;
            }
        }

        // No timestamp header was found.  Column 0 still labels the row in the
        // report, but it is not a time column and must never drive footer
        // detection - it usually holds a measuring-point name.
        matchedTimeHeader = false;
        return 0;
    }

    private static readonly string[] StatisticsFooterLabels =
    [
        "最大值",
        "最小值",
        "平均值",
        "合计",
        "总计",
        "Total",
        "Max",
        "Min",
        "Maximum",
        "Minimum",
        "Average",
        "Avg",
        "Sum",
    ];

    // Anchored on the whole cell: a footer label is the entire value of the time
    // cell, never a substring of a measuring-point name such as "MIN-01柜",
    // "Terminal-3" or "Total Feeder".
    private static bool IsStatisticsTailRow(string value)
    {
        var trimmed = value.Trim().TrimEnd(':', '：').TrimEnd();
        return trimmed.Length > 0
            && StatisticsFooterLabels.Any(label =>
                string.Equals(trimmed, label, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> SplitCsvLine(string line)
    {
        var cells = new List<string>();
        var current = new List<char>();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var currentChar = line[index];
            if (currentChar == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Add('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (currentChar == ',' && !inQuotes)
            {
                cells.Add(new string(current.ToArray()).Trim());
                current.Clear();
            }
            else
            {
                current.Add(currentChar);
            }
        }

        cells.Add(new string(current.ToArray()).Trim());
        return cells;
    }

    private static bool TryParseDouble(string value, out double result)
    {
        var parsed = double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result)
            || double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out result);
        if (parsed && double.IsFinite(result))
        {
            return true;
        }

        result = 0;
        return false;
    }

    private static bool IsInvalidValue(double value) =>
        Math.Abs(value - -1.0) < 0.000_001
        || Math.Abs(value - 2867.2) < 0.000_001;

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToList();
        if (ordered.Count == 0)
        {
            return 0;
        }

        var middle = ordered.Count / 2;
        return ordered.Count % 2 == 1
            ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) / 2.0;
    }

    private static double GetFiniteDouble(JsonElement root, string key, double fallback)
    {
        if (!root.TryGetProperty(key, out var value))
        {
            return fallback;
        }

        var parsed = value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when TryParseDouble(value.GetString() ?? "", out var stringValue) => stringValue,
            _ => fallback,
        };
        return double.IsFinite(parsed) ? parsed : fallback;
    }

    private static bool GetBool(JsonElement root, string key, bool fallback)
    {
        if (!root.TryGetProperty(key, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback,
        };
    }

    private static int GetPositiveInt(JsonElement root, string key, int fallback)
    {
        if (!root.TryGetProperty(key, out var value))
        {
            return fallback;
        }

        var parsed = value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt32(),
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var stringValue) => stringValue,
            _ => fallback,
        };
        return parsed > 0 ? parsed : fallback;
    }

}
