using EDetection.Desktop.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace EDetection.Desktop.Views;

internal static class PickerFailureFeedback
{
    /// <summary>
    /// Reports a failed or cancelled picker on the surface that is actually
    /// visible.  The settings footer is hidden while the run-setup pane is
    /// shown, so run-setup failures go to the setup pane's own InfoBar.
    /// </summary>
    public static void Report(MainViewModel? viewModel, string action, Exception exception)
    {
        if (viewModel is null)
        {
            return;
        }

        var message = $"{action}: {exception.Message}";
        viewModel.RunSetupFeedbackText = message;
        viewModel.RunSetupFeedbackSeverity = InfoBarSeverity.Warning;
        viewModel.IsRunSetupFeedbackOpen = true;
        viewModel.AddRuntimeLog("文件选择提醒", message);
    }

    public static void ReportInSettings(MainViewModel? viewModel, string action, Exception exception)
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
