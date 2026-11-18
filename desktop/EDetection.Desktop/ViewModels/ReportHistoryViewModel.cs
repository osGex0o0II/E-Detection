using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EDetection.Desktop.Models;
using EDetection.Desktop.Services;
using Microsoft.UI.Xaml;

namespace EDetection.Desktop.ViewModels;

public sealed partial class ReportHistoryViewModel(
    ReportHistoryService history) : ObservableObject
{
    private bool _loading;

    public ObservableCollection<RecentReport> RecentReports { get; } = [];

    public ObservableCollection<RecentReport> FilteredRecentReports { get; } = [];

    public int RecentReportLimit => SelectedRecentReportLimitIndex switch
    {
        0 => 10,
        2 => 50,
        _ => 20,
    };

    public string RecentReportLimitText => $"保留最近 {RecentReportLimit} 个";

    // Lowering the limit deletes history immediately, so the count that was
    // dropped is surfaced instead of letting entries vanish silently.
    public int LastTrimmedReportCount { get; private set; }

    public string TrimWarningText => LastTrimmedReportCount > 0
        ? $"已按新上限移除 {LastTrimmedReportCount} 个较早的报告记录。"
        : "";

    public Visibility TrimWarningVisibility => LastTrimmedReportCount > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string StatusText => RecentReports.Count > 0
        ? $"{FilteredRecentReports.Count}/{RecentReports.Count} 个报告"
        : "暂无报告历史";

    // The clear action must not be clickable when there is nothing to clear;
    // a confirmation dialog that then does nothing reads as a broken action.
    public bool HasReports => RecentReports.Count > 0;

    public Visibility EmptyStateVisibility => FilteredRecentReports.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string EmptyStateTitle => RecentReports.Count == 0
        ? "暂无报告历史"
        : "没有匹配的报告";

    public string EmptyStateHint => RecentReports.Count == 0
        ? "完成一次检测后，最近报告会显示在这里。"
        : "调整搜索关键词或筛选条件后再试。";

    [ObservableProperty]
    public partial RecentReport? SelectedReport { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecentReportLimit))]
    [NotifyPropertyChangedFor(nameof(RecentReportLimitText))]
    public partial int SelectedRecentReportLimitIndex { get; set; } = 1;

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial int SelectedFilterIndex { get; set; }

    public void Load(
        IEnumerable<RecentReport> reports,
        int selectedRecentReportLimitIndex)
    {
        _loading = true;
        SelectedRecentReportLimitIndex = Math.Clamp(selectedRecentReportLimitIndex, 0, 2);
        _loading = false;

        history.Load(RecentReports, reports, RecentReportLimit);
        Refresh();
        NotifyStatusChanged();
    }

    public bool AddOrUpdate(
        string path,
        DetectionBackendEvent? evt,
        RecentReportUpdateContext context)
    {
        var item = history.AddOrUpdate(
            RecentReports,
            path,
            evt,
            context,
            RecentReportLimit,
            SelectedReport,
            out var clearedSelectedReport);
        if (item is null)
        {
            return false;
        }

        if (clearedSelectedReport)
        {
            SelectedReport = null;
        }

        Refresh();
        NotifyStatusChanged();
        return true;
    }

    public bool RemoveSelected()
    {
        if (SelectedReport is null)
        {
            return false;
        }

        var removed = history.Remove(RecentReports, SelectedReport, RecentReportLimit);
        if (!removed)
        {
            return false;
        }

        SelectedReport = null;
        Refresh();
        NotifyStatusChanged();
        return true;
    }

    public void Clear()
    {
        history.Clear(RecentReports);
        FilteredRecentReports.Clear();
        SelectedReport = null;
        NotifyStatusChanged();
    }

    public void Refresh()
    {
        var result = history.Filter(
            RecentReports,
            SelectedReport,
            SearchText,
            SelectedFilterIndex);
        FilteredRecentReports.Clear();
        foreach (var report in result.FilteredReports)
        {
            FilteredRecentReports.Add(report);
        }

        if (!ReferenceEquals(SelectedReport, result.SelectedReport))
        {
            SelectedReport = result.SelectedReport;
        }

        NotifyStatusChanged();
    }

    partial void OnSelectedRecentReportLimitIndexChanged(int value)
    {
        if (_loading)
        {
            return;
        }

        var before = RecentReports.Count;
        history.Trim(RecentReports, RecentReportLimit);
        LastTrimmedReportCount = before - RecentReports.Count;
        OnPropertyChanged(nameof(LastTrimmedReportCount));
        OnPropertyChanged(nameof(TrimWarningText));
        OnPropertyChanged(nameof(TrimWarningVisibility));
        history.RefreshLatestMarkers(RecentReports);
        Refresh();
        NotifyStatusChanged();
    }

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnSelectedFilterIndexChanged(int value) => Refresh();

    private void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(EmptyStateVisibility));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateHint));
        OnPropertyChanged(nameof(HasReports));
    }
}
