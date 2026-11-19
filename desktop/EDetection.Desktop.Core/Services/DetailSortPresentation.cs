using EDetection.NativeCore.Models;

namespace EDetection.Desktop.Services;

/// <summary>
/// Presentation rules for the detail table's sort state and severity picking.
/// Kept in the WinUI-free assembly so the ordering and arrow behaviour can be
/// unit tested without starting the desktop shell.
/// </summary>
public static class DetailSortPresentation
{
    /// <summary>
    /// Column header label for the active sort, with the direction arrow.
    /// Time sorts newest-first by nature, so its arrow is inverted relative to
    /// the raw descending flag.
    /// </summary>
    public static string HeaderText(string label, string? activeSortKey, string columnKey, bool descending)
    {
        if (!string.Equals(activeSortKey, columnKey, StringComparison.Ordinal))
        {
            return label;
        }

        return IsDescending(columnKey, descending) ? $"{label} ↓" : $"{label} ↑";
    }

    /// <summary>Screen-reader sort status for the active column.</summary>
    public static string SortStatusText(string label, string? activeSortKey, string columnKey, bool descending) =>
        IsDescending(columnKey, descending) ? $"按{label}降序" : $"按{label}升序";

    /// <summary>
    /// True when the column's effective order is descending.  Time's natural
    /// order is newest-first, so an unset flag already means descending.
    /// </summary>
    public static bool IsDescending(string columnKey, bool descending) =>
        string.Equals(columnKey, "time", StringComparison.Ordinal) ? !descending : descending;

    public static int SeverityRank(string? severity) => severity switch
    {
        "高" => 0,
        "中" => 1,
        "低" => 2,
        _ => 3,
    };

    /// <summary>
    /// Highest severity first, earliest time within the same severity.  The
    /// "locate first anomaly" action used the first row of the current filter
    /// order, which pointed at the newest row instead of the worst one.
    /// </summary>
    public static ReportDetailPreview? MostSevere(IEnumerable<ReportDetailPreview> details) =>
        details
            .OrderBy(static detail => SeverityRank(detail.Severity))
            .ThenBy(static detail => detail.TimeText, StringComparer.Ordinal)
            .FirstOrDefault();
}
