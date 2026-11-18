using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EDetection.Desktop.Models;
using EDetection.Desktop.Services;
using Microsoft.UI.Xaml;

namespace EDetection.Desktop.ViewModels;

public sealed partial class RuntimeLogViewModel(
    RuntimeLogService logs) : ObservableObject
{
    public ObservableCollection<DetectionLogItem> LogItems { get; } = [];

    public ObservableCollection<DetectionLogItem> FilteredLogItems { get; } = [];

    public ObservableCollection<string> LogKindFilters { get; } = ["全部类型"];

    public int RetentionLimit => SelectedRetentionIndex switch
    {
        0 => 200,
        2 => 1000,
        3 => 2000,
        _ => 500,
    };

    public string RetentionText => $"保留最近 {RetentionLimit} 条";

    // Lowering the retention limit deletes log rows immediately.
    public int LastTrimmedLogCount { get; private set; }

    public string TrimWarningText => LastTrimmedLogCount > 0
        ? $"已按新上限移除 {LastTrimmedLogCount} 条较早的运行记录。"
        : "";

    public Visibility TrimWarningVisibility => LastTrimmedLogCount > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string StatusText => LogItems.Count > 0
        ? $"{FilteredLogItems.Count}/{LogItems.Count} 条记录"
        : "暂无记录";

    // Clearing is destructive and must not stay clickable on an empty log.
    public bool HasLogs => LogItems.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RetentionLimit))]
    [NotifyPropertyChangedFor(nameof(RetentionText))]
    public partial int SelectedRetentionIndex { get; set; } = 1;

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial int SelectedKindFilterIndex { get; set; }

    public void Add(string kind, string message)
    {
        logs.Add(LogItems, kind, message, RetentionLimit);
        Trim();
        Refresh();
    }

    public void Trim()
    {
        logs.Trim(LogItems, RetentionLimit);
        NotifyStatusChanged();
    }

    public void Refresh()
    {
        var result = logs.Refresh(
            LogItems,
            LogKindFilters,
            SelectedKindFilterIndex,
            SearchText);
        LogKindFilters.Clear();
        foreach (var kind in result.KindFilters)
        {
            LogKindFilters.Add(kind);
        }

        if (SelectedKindFilterIndex != result.SelectedKindFilterIndex)
        {
            SelectedKindFilterIndex = result.SelectedKindFilterIndex;
        }

        FilteredLogItems.Clear();
        foreach (var item in result.FilteredItems)
        {
            FilteredLogItems.Add(item);
        }

        NotifyStatusChanged();
    }

    public void ClearFilters()
    {
        SearchText = "";
        SelectedKindFilterIndex = 0;
    }

    public void Clear()
    {
        logs.Clear(LogItems, FilteredLogItems, LogKindFilters);
        SearchText = "";
        SelectedKindFilterIndex = 0;
        NotifyStatusChanged();
    }

    public string BuildExportText() =>
        logs.BuildExportText(FilteredLogItems);

    partial void OnSelectedRetentionIndexChanged(int value)
    {
        var before = LogItems.Count;
        Trim();
        LastTrimmedLogCount = before - LogItems.Count;
        Refresh();
        OnPropertyChanged(nameof(LastTrimmedLogCount));
        OnPropertyChanged(nameof(TrimWarningText));
        OnPropertyChanged(nameof(TrimWarningVisibility));
    }

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnSelectedKindFilterIndexChanged(int value) => Refresh();

    private void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasLogs));
    }
}
