using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace EDetection.Desktop.Views;

public sealed partial class SettingsView
{
    private async void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmActionAsync(
                "恢复默认设置？",
                "将恢复检测阈值、目录、窗口和网络偏好。已保存的 API Key、Token 与代理密码不会删除。",
                "恢复默认"))
        {
            return;
        }

        ViewModel?.ResetSettingsToDefaultsCommand.Execute(null);
    }

    private async void ClearRecentReports_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmActionAsync("清空报告历史？", "这会移除应用中的报告历史记录，不会删除磁盘上的 Excel 报告。", "清空历史"))
        {
            ViewModel?.ClearRecentReportsCommand.Execute(null);
        }
    }

    private async void ClearRuntimeLogs_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmActionAsync("清空运行记录？", "这会移除应用中的运行记录，无法恢复。", "清空记录"))
        {
            ViewModel?.ClearRuntimeLogsCommand.Execute(null);
        }
    }

    private async Task<bool> ConfirmActionAsync(string title, string content, string primaryButtonText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void BrowseConfigPath_Click(object sender, RoutedEventArgs e)
    {
        var file = await PickFileAsync(".json");
        if (file is not null && ViewModel is not null)
        {
            ViewModel.PendingConfigPath = file.Path;
            await LoadPendingConfigWithConfirmationAsync();
        }
    }

    private async void LoadDetectionConfig_Click(object sender, RoutedEventArgs e) =>
        await LoadPendingConfigWithConfirmationAsync();

    private async Task LoadPendingConfigWithConfirmationAsync()
    {
        if (ViewModel is null || ViewModel.IsRunning)
        {
            return;
        }

        if (ViewModel.IsDetectionConfigDirty
            && !await ConfirmActionAsync(
                "放弃未保存的检测阈值？",
                "加载另一份配置会替换当前尚未保存的阈值；原文件不会因此被覆盖。",
                "放弃修改并加载"))
        {
            return;
        }

        ViewModel.TryLoadPendingDetectionConfig(discardUnsavedChanges: true);
    }

    private async void SaveDetectionConfigAs_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || ViewModel.IsRunning)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedFileName = "detection-config",
            DefaultFileExtension = ".json",
        };
        picker.FileTypeChoices.Add("JSON 配置", [".json"]);
        if (App.CurrentWindow is not null)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.CurrentWindow));
        }

        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            ViewModel.TrySaveDetectionConfigAs(file.Path);
        }
    }

    private static async Task<StorageFile?> PickFileAsync(params string[] fileTypes)
    {
        var picker = new FileOpenPicker();
        foreach (var fileType in fileTypes)
        {
            picker.FileTypeFilter.Add(fileType);
        }

        if (App.CurrentWindow is not null)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.CurrentWindow));
        }

        return await picker.PickSingleFileAsync();
    }
}
