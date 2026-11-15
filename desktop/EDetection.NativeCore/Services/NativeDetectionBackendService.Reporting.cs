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
    private static string WriteNativeExcelReport(
        string outputRoot,
        string generatedAt,
        DetectionBackendEvent summary,
        NativeDetectionConfig config,
        IReadOnlyList<ReportDetailPreview> reportDetails,
        IReadOnlyList<NativeSensorStatusRow> sensorRows,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(outputRoot);
        var reportPath = GetUniqueReportPath(outputRoot);
        var temporaryPath = Path.Combine(
            outputRoot,
            $".{Path.GetFileName(reportPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
            {
                AddZipEntry(
                    archive,
                    "[Content_Types].xml",
                    """
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/worksheets/sheet3.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/worksheets/sheet4.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/worksheets/sheet5.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/worksheets/sheet6.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            </Types>
            """);
                AddZipEntry(
                    archive,
                    "_rels/.rels",
                    """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);
                AddZipEntry(archive, "xl/workbook.xml", BuildWorkbookXml());
                AddZipEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationshipsXml());
                AddZipEntry(archive, "xl/styles.xml", BuildStylesXml());

                var details = reportDetails;
                cancellationToken.ThrowIfCancellationRequested();
                AddZipEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(BuildOverviewRows(summary, generatedAt), cancellationToken));
                AddZipEntry(archive, "xl/worksheets/sheet2.xml", BuildDetailWorksheetXml(details, cancellationToken));
                AddZipEntry(archive, "xl/worksheets/sheet3.xml", BuildWorksheetXml(BuildDeviceRows(details, cancellationToken), cancellationToken));
                AddZipEntry(archive, "xl/worksheets/sheet4.xml", BuildWorksheetXml(BuildIssueRows(details, cancellationToken), cancellationToken));
                AddZipEntry(archive, "xl/worksheets/sheet5.xml", BuildWorksheetXml(BuildSensorRows(sensorRows, cancellationToken), cancellationToken));
                AddZipEntry(archive, "xl/worksheets/sheet6.xml", BuildWorksheetXml(BuildConfigRows(config, summary, generatedAt), cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
            }

            ValidateWorkbookArchive(temporaryPath);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, reportPath);
            return reportPath;
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            throw;
        }
    }

    private static void ValidateWorkbookArchive(string path)
    {
        string[] requiredEntries =
        [
            "[Content_Types].xml",
            "_rels/.rels",
            "xl/workbook.xml",
            "xl/_rels/workbook.xml.rels",
            "xl/styles.xml",
            "xl/worksheets/sheet1.xml",
            "xl/worksheets/sheet2.xml",
            "xl/worksheets/sheet3.xml",
            "xl/worksheets/sheet4.xml",
            "xl/worksheets/sheet5.xml",
            "xl/worksheets/sheet6.xml",
        ];
        using var archive = ZipFile.OpenRead(path);
        foreach (var entryName in requiredEntries)
        {
            var entry = archive.GetEntry(entryName);
            if (entry is null)
            {
                throw new InvalidDataException($"报告工作簿缺少条目: {entryName}");
            }

            // Presence is not validity.  Parsing each part is what makes this a
            // gate: a workbook that Excel cannot open must never be published
            // and announced as a completed report.
            try
            {
                using var stream = entry.Open();
                _ = System.Xml.Linq.XDocument.Load(stream);
            }
            catch (System.Xml.XmlException ex)
            {
                throw new InvalidDataException($"报告工作簿条目不是合法 XML: {entryName} ({ex.Message})", ex);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or NotSupportedException)
        {
        }
    }

    private static string GetUniqueReportPath(string outputRoot)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(outputRoot, $"电气异常报告_{timestamp}.xlsx");
        if (!File.Exists(path))
        {
            return path;
        }

        for (var index = 1; index < 100; index++)
        {
            var candidate = Path.Combine(outputRoot, $"电气异常报告_{timestamp}_{index}.xlsx");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(outputRoot, $"电气异常报告_{timestamp}_{Guid.NewGuid():N}.xlsx");
    }

    private static void AddZipEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string BuildWorkbookXml() =>
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="检测概览" sheetId="1" r:id="rId1"/>
            <sheet name="异常明细" sheetId="2" r:id="rId2"/>
            <sheet name="设备汇总" sheetId="3" r:id="rId3"/>
            <sheet name="异常分类统计" sheetId="4" r:id="rId4"/>
            <sheet name="传感器状态" sheetId="5" r:id="rId5"/>
            <sheet name="检测配置" sheetId="6" r:id="rId6"/>
          </sheets>
        </workbook>
        """;

    private static string BuildWorkbookRelationshipsXml() =>
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
          <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet3.xml"/>
          <Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet4.xml"/>
          <Relationship Id="rId5" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet5.xml"/>
          <Relationship Id="rId6" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet6.xml"/>
          <Relationship Id="rId7" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    private static string BuildStylesXml() =>
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <fonts count="2">
            <font><sz val="11"/><name val="Calibri"/></font>
            <font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Calibri"/></font>
          </fonts>
          <fills count="7">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="gray125"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FF1F4E78"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFF4CCCC"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFFCE5CD"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFD9EAD3"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFFFF2CC"/><bgColor indexed="64"/></patternFill></fill>
          </fills>
          <borders count="2">
            <border><left/><right/><top/><bottom/><diagonal/></border>
            <border><left style="thin"><color rgb="FFD9E2F3"/></left><right style="thin"><color rgb="FFD9E2F3"/></right><top style="thin"><color rgb="FFD9E2F3"/></top><bottom style="thin"><color rgb="FFD9E2F3"/></bottom><diagonal/></border>
          </borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="6">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="0" fillId="3" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="0" fillId="4" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="0" fillId="5" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="0" fillId="6" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
          </cellXfs>
          <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
        </styleSheet>
        """;

    private static string BuildWorksheetXml(
        IEnumerable<IReadOnlyList<object?>> rows,
        CancellationToken cancellationToken,
        Func<int, int, object?, int?>? styleSelector = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var materializedRows = rows.ToList();
        cancellationToken.ThrowIfCancellationRequested();
        var maxColumns = materializedRows.Count == 0 ? 0 : materializedRows.Max(static row => row.Count);
        var maxRows = materializedRows.Count;
        var builder = new StringBuilder();
        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        builder.AppendLine("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        builder.AppendLine("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");
        AppendColumns(builder, materializedRows, maxColumns, cancellationToken);
        builder.AppendLine("<sheetData>");

        var rowIndex = 1;
        foreach (var row in materializedRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowIndex}\">");
            for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = row[columnIndex];
                var styleIndex = rowIndex == 1
                    ? 1
                    : styleSelector?.Invoke(rowIndex, columnIndex + 1, value) ?? 0;
                AppendCell(builder, value, ColumnName(columnIndex + 1), rowIndex, styleIndex);
            }

            builder.AppendLine("</row>");
            rowIndex++;
        }

        builder.AppendLine("</sheetData>");
        if (maxColumns > 1 && maxRows > 1)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"<autoFilter ref=\"A1:{ColumnName(maxColumns)}{maxRows}\"/>");
        }

        builder.AppendLine("</worksheet>");
        return builder.ToString();
    }

    private static string BuildDetailWorksheetXml(
        IReadOnlyList<ReportDetailPreview> details,
        CancellationToken cancellationToken)
    {
        var rows = BuildDetailRows(details, cancellationToken);
        return BuildWorksheetXml(
            rows,
            cancellationToken,
            (rowIndex, columnIndex, _) =>
            {
                if (rowIndex < 2 || rowIndex - 2 >= details.Count)
                {
                    return 0;
                }

                var detail = details[rowIndex - 2];
                var reportValueColumnIndex = columnIndex - 11;
                if (reportValueColumnIndex >= 0
                    && reportValueColumnIndex < ReportValueColumns.Length
                    && IsAffectedReportColumn(detail, ReportValueColumns[reportValueColumnIndex]))
                {
                    return 5;
                }

                return SeverityStyleIndex(detail.Severity);
            });
    }

    private static void AppendColumns(
        StringBuilder builder,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        int maxColumns,
        CancellationToken cancellationToken)
    {
        if (maxColumns == 0)
        {
            return;
        }

        builder.AppendLine("<cols>");
        for (var columnIndex = 1; columnIndex <= maxColumns; columnIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var width = 10;
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (columnIndex - 1 < row.Count)
                {
                    var value = row[columnIndex - 1]?.ToString() ?? "";
                    width = Math.Max(width, Math.Min(value.Length + 2, 42));
                }
            }

            builder.AppendLine(CultureInfo.InvariantCulture, $"<col min=\"{columnIndex}\" max=\"{columnIndex}\" width=\"{width}\" customWidth=\"1\"/>");
        }

        builder.AppendLine("</cols>");
    }

    private static void AppendCell(StringBuilder builder, object? value, string columnName, int rowIndex, int styleIndex)
    {
        var cellReference = $"{columnName}{rowIndex}";
        var styleAttribute = styleIndex > 0 ? $" s=\"{styleIndex}\"" : "";
        switch (value)
        {
            case null:
                builder.Append(CultureInfo.InvariantCulture, $"<c r=\"{cellReference}\"{styleAttribute}/>");
                break;
            case int or long or double or float or decimal:
                builder.Append(CultureInfo.InvariantCulture, $"<c r=\"{cellReference}\"{styleAttribute}><v>{Convert.ToString(value, CultureInfo.InvariantCulture)}</v></c>");
                break;
            default:
                builder.Append(CultureInfo.InvariantCulture, $"<c r=\"{cellReference}\"{styleAttribute} t=\"inlineStr\"><is><t>{EscapeXml(EscapeExcelFormulaText(value.ToString() ?? ""))}</t></is></c>");
                break;
        }
    }

    private static string ColumnName(int columnNumber)
    {
        var name = "";
        while (columnNumber > 0)
        {
            columnNumber--;
            name = (char)('A' + columnNumber % 26) + name;
            columnNumber /= 26;
        }

        return name;
    }

    private static string EscapeXml(string value) =>
        SecurityElement.Escape(RemoveInvalidXmlCharacters(value)) ?? "";

    // XML 1.0 cannot represent most C0 control characters at all - not even as
    // numeric character references - so escaping them is not an option: a stray
    // 0x01 in a SCADA export produced a workbook Excel refused to open, while
    // the app reported success.  They are dropped instead.  Tab, LF and CR are
    // legal in XML and meaningful inside a cell, so they are preserved.
    private static string RemoveInvalidXmlCharacters(string value)
    {
        var hasInvalid = false;
        foreach (var current in value)
        {
            if (!System.Xml.XmlConvert.IsXmlChar(current))
            {
                hasInvalid = true;
                break;
            }
        }

        if (!hasInvalid)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (index + 1 < value.Length
                && char.IsHighSurrogate(current)
                && char.IsLowSurrogate(value[index + 1])
                && System.Xml.XmlConvert.IsXmlSurrogatePair(value[index + 1], current))
            {
                // A well-formed astral character (emoji, rare CJK) is kept whole;
                // an unpaired surrogate half is not representable and is dropped.
                builder.Append(current).Append(value[index + 1]);
                index++;
                continue;
            }

            if (System.Xml.XmlConvert.IsXmlChar(current))
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }

    private static string EscapeExcelFormulaText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var stripped = value.TrimStart(' ');
        return value[0] is '\t' or '\r' or '\n'
               || stripped.StartsWith("=", StringComparison.Ordinal)
               || stripped.StartsWith("+", StringComparison.Ordinal)
               || stripped.StartsWith("-", StringComparison.Ordinal)
               || stripped.StartsWith("@", StringComparison.Ordinal)
            ? $"'{value}"
            : value;
    }

    private static int SeverityStyleIndex(string? severity) =>
        severity switch
        {
            "高" => 2,
            "中" => 3,
            "低" => 4,
            _ => 0,
        };

    private static bool IsAffectedReportColumn(ReportDetailPreview detail, string column)
    {
        var issueText = $"{detail.IssueType} {detail.IssueDetail}";
        if (!detail.ReportValues.ContainsKey(column))
        {
            return false;
        }

        if ((issueText.Contains("电压", StringComparison.Ordinal)
             || issueText.Contains("PT", StringComparison.Ordinal))
            && column is "Uab" or "Ubc" or "Uca")
        {
            return true;
        }

        if (issueText.Contains("电流", StringComparison.Ordinal)
            && column is "Ia" or "Ib" or "Ic")
        {
            return true;
        }

        if ((issueText.Contains("CT", StringComparison.Ordinal)
             || issueText.Contains("有功", StringComparison.Ordinal))
            && column is "Ia" or "Ib" or "Ic" or "有功功率")
        {
            return true;
        }

        if (issueText.Contains("功率因数", StringComparison.Ordinal)
            && column == "功率因数")
        {
            return true;
        }

        if ((issueText.Contains("温度", StringComparison.Ordinal)
             || issueText.Contains("传感器", StringComparison.Ordinal))
            && column is "A相温度" or "B相温度" or "C相温度")
        {
            return true;
        }

        if (issueText.Contains("冻结", StringComparison.Ordinal)
            || issueText.Contains("恒定", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildOverviewRows(
        DetectionBackendEvent summary,
        string generatedAt)
    {
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "指标", "值" },
            new object?[] { "输入目录", summary.InputDirectory },
            new object?[] { "输出目录", summary.OutputDirectory },
            new object?[] { "文件总数", summary.TotalFiles },
            new object?[] { "已处理文件", summary.ProcessedFiles },
            new object?[] { "正常文件", summary.NormalFiles },
            new object?[] { "异常文件", summary.AnomalyFiles },
            new object?[] { "异常记录", summary.AnomalyRecords },
            new object?[] { "跳过文件", summary.SkippedFiles },
            new object?[] { "生成时间", generatedAt },
            Array.Empty<object?>(),
            new object?[] { "高风险设备 Top 10", "异常记录数" },
        };

        foreach (var device in summary.HighRiskDevices ?? [])
        {
            rows.Add(new object?[] { $"{device.Building} / {device.Transformer} ({device.DevicePath})", device.AnomalyRecords });
        }

        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildDetailRows(
        IReadOnlyList<ReportDetailPreview> details,
        CancellationToken cancellationToken)
    {
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "建筑", "相对路径", "变压器", "来源文件", "日期", "时间", "严重等级", "异常类型", "异常详情", "异常值" }
                .Concat(ReportValueColumns.Cast<object?>())
                .Concat(new object?[] { "建议处置" })
                .ToArray(),
        };
        foreach (var detail in details)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(new object?[]
                {
                    detail.Building,
                    detail.RelativePath,
                    detail.Transformer,
                    detail.SourceFile,
                    detail.Date,
                    detail.Time,
                    detail.Severity,
                    detail.IssueType,
                    detail.IssueDetail,
                    detail.IssueValue,
                }
                .Concat(ReportValueColumns.Select(column =>
                    detail.ReportValues.TryGetValue(column, out var value) ? value : (object?)null))
                .Concat(new object?[] { detail.RecommendedAction })
                .ToArray());
        }

        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildDeviceRows(
        IReadOnlyList<ReportDetailPreview> details,
        CancellationToken cancellationToken)
    {
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "建筑", "设备路径", "变压器", "异常记录数", "主要异常类型", "首次异常时间", "末次异常时间", "最高严重等级", "建议优先级" },
        };
        foreach (var device in BuildDeviceSummaries(details, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(new object?[]
            {
                device.Building,
                device.DevicePath,
                device.Transformer,
                device.AnomalyRecords,
                device.MainIssueTypes,
                FirstDetailTime(details, device.Building, device.DevicePath, device.Transformer),
                LastDetailTime(details, device.Building, device.DevicePath, device.Transformer),
                device.HighestSeverity,
                device.Priority,
            });
        }

        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildIssueRows(
        IReadOnlyList<ReportDetailPreview> details,
        CancellationToken cancellationToken)
    {
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "统计维度", "名称", "数量" },
        };
        foreach (var issue in BuildIssueTypeStatistics(details))
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(new object?[] { "异常类型", issue.Name, issue.Count });
        }

        foreach (var group in details
                     .GroupBy(static detail => string.IsNullOrWhiteSpace(detail.Building) ? "根目录" : detail.Building)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(new object?[] { "建筑", group.Key, group.Count() });
        }

        foreach (var group in details
                     .GroupBy(static detail => string.IsNullOrWhiteSpace(detail.Transformer) ? "未知" : detail.Transformer)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(new object?[] { "变压器", group.Key, group.Count() });
        }

        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildSensorRows(
        IReadOnlyList<NativeSensorStatusRow> sensorRows,
        CancellationToken cancellationToken)
    {
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "来源文件", "建筑", "相对路径", "变压器", "是否离线", "传感器故障", "传感器未配置", "状态", "原因" },
        };
        foreach (var row in sensorRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(new object?[]
            {
                row.SourceFile,
                row.Building,
                row.RelativePath,
                row.Transformer,
                row.IsOffline ? "是" : "否",
                string.Join("；", row.SensorFaults),
                string.Join("；", row.SensorMissing),
                row.Status,
                row.Reason,
            });
        }

        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildConfigRows(
        NativeDetectionConfig config,
        DetectionBackendEvent summary,
        string generatedAt)
    {
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "配置项", "值" },
            new object?[] { "backend_mode", "native" },
            new object?[] { "report_backend", nameof(NativeDetectionBackendService) },
            new object?[] { "V_MIN_THRESHOLD", config.VoltageMinThreshold },
            new object?[] { "V_MAX_THRESHOLD", config.VoltageMaxThreshold },
            new object?[] { "V_IMBALANCE_THRESHOLD", config.VoltageImbalanceThreshold },
            new object?[] { "I_MAX_THRESHOLD", config.CurrentMaxThreshold },
            new object?[] { "I_UNBALANCE_MAX_THRESHOLD", config.CurrentUnbalanceMaxThreshold },
            new object?[] { "I_MIN_ACTIVE_THRESHOLD", config.CurrentActiveMinThreshold },
            new object?[] { "P_ACTIVE_MIN_THRESHOLD", config.ActivePowerMinThreshold },
            new object?[] { "PF_MIN_THRESHOLD", config.PowerFactorMinThreshold },
            new object?[] { "T_MIN_THRESHOLD", config.TemperatureMinThreshold },
            new object?[] { "T_MAX_THRESHOLD", config.TemperatureMaxThreshold },
            new object?[] { "current_overload", config.CurrentOverloadEnabled.ToString(CultureInfo.InvariantCulture) },
            new object?[] { "current_unbalance", config.CurrentUnbalanceEnabled.ToString(CultureInfo.InvariantCulture) },
            new object?[] { "power_factor", config.PowerFactorEnabled.ToString(CultureInfo.InvariantCulture) },
            new object?[] { "detail_output", config.DetailOutputEnabled.ToString(CultureInfo.InvariantCulture) },
            new object?[] { "FREEZE_COUNT_THRESHOLD", config.FreezeCountThreshold },
            new object?[] { "FREEZE_STD_THRESHOLD", config.FreezeStdThreshold },
            Array.Empty<object?>(),
            new object?[] { "运行信息", "值" },
            new object?[] { "input_dir", summary.InputDirectory },
            new object?[] { "output_dir", summary.OutputDirectory },
            new object?[] { "total_files", summary.TotalFiles },
            new object?[] { "processed_files", summary.ProcessedFiles },
            new object?[] { "normal_files", summary.NormalFiles },
            new object?[] { "anomaly_files", summary.AnomalyFiles },
            new object?[] { "anomaly_records", summary.AnomalyRecords },
            new object?[] { "skipped_files", summary.SkippedFiles },
            new object?[] { "generated_at", generatedAt },
        };
        return rows;
    }

}
