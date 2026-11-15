using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

internal static partial class RegressionSuite
{
    // The report is the product.  A cell value carrying a C0 control character
    // reached the sheet XML with only entity escaping - and XML 1.0 cannot
    // represent those characters at all, not even as numeric references - so
    // Excel refused the file.  ValidateWorkbookArchive only checked that entries
    // existed, never that they parsed, so the app published the corrupt
    // workbook, raised report_written and reported success.
    private static async Task ControlCharactersProduceReadableWorkbookAsync()
    {
        var root = TestSupport.CreateFixtureDirectory("ControlCharacterReport");
        var output = Path.Combine(root, "reports");
        try
        {
            // U+0001 inside the time cell, U+000C inside a second row: both are
            // legal bytes in a CSV export and both are forbidden in XML 1.0.
            const char startOfHeading = '\u0001';
            const char formFeed = '\u000C';
            var csv =
                "time,Uab,Ubc,Uca\n"
                + $"01:{startOfHeading}00,320,380,380\n"
                + $"02:{formFeed}00,310,380,380\n";
            File.WriteAllText(Path.Combine(root, "20260712_1TM.csv"), csv, Encoding.UTF8);

            var (exitCode, events) = await RunBackendAsync(
                root,
                outputDirectory: output,
                writeReport: true);
            TestSupport.Equal(0, exitCode, "A control character must not fail the run.");

            var reportPath = events.Single(e => e.EventName == "report_written").ReportPath
                ?? throw new InvalidOperationException("The report-written event must include a path.");

            using var archive = ZipFile.OpenRead(reportPath);
            string[] xmlEntries =
            [
                "[Content_Types].xml",
                "xl/workbook.xml",
                "xl/worksheets/sheet1.xml",
                "xl/worksheets/sheet2.xml",
                "xl/worksheets/sheet3.xml",
            ];

            foreach (var entryName in xmlEntries)
            {
                var entry = archive.GetEntry(entryName)
                    ?? throw new InvalidOperationException($"The report must contain {entryName}.");
                using var stream = entry.Open();
                try
                {
                    _ = XDocument.Load(stream);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"A published workbook must be well-formed XML, but {entryName} does not parse: {ex.Message}");
                }
            }
        }
        finally
        {
            TestSupport.DeleteFixtureDirectory(root);
        }
    }
}
