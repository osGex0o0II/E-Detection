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

    private async void BrowseInputDirectory_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is null || ViewModel is null)
        {
            return;
        }

        ViewModel.InputDirectory = folder.Path;
        if (string.IsNullOrWhiteSpace(ViewModel.OutputDirectory))
        {
            ViewModel.OutputDirectory = folder.Path;
        }
    }

    private async void BrowseOutputDirectory_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null && ViewModel is not null)
        {
            ViewModel.OutputDirectory = folder.Path;
        }
    }

    private async void BrowseConfigPath_Click(object sender, RoutedEventArgs e)
    {
        var file = await PickFileAsync(".json");
        if (file is not null && ViewModel is not null)
        {
            ViewModel.ConfigPath = file.Path;
            ViewModel.LoadDetectionConfigFromPathCommand.Execute(null);
        }
    }

    private static async Task<StorageFolder?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        if (App.CurrentWindow is not null)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.CurrentWindow));
        }

        return await picker.PickSingleFolderAsync();
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
