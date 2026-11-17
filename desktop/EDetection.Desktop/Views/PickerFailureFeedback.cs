using EDetection.Desktop.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace EDetection.Desktop.Views;

internal static class PickerFailureFeedback
{
    public static void Report(MainViewModel? viewModel, string action, Exception exception)
    {
        if (viewModel is null)
        {
            return;
        }

        var message = $"{action}: {exception.Message}";
        viewModel.SettingsFeedbackText = message;
        viewModel.SettingsFeedbackSeverity = InfoBarSeverity.Warning;
        viewModel.IsSettingsFeedbackOpen = true;
        viewModel.AddRuntimeLog("文件选择提醒", message);
    }
}
