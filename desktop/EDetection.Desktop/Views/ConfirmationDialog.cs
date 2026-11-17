using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EDetection.Desktop.Views;

internal static class ConfirmationDialog
{
    public static async Task<bool> ConfirmDestructiveActionAsync(
        XamlRoot? xamlRoot,
        FrameworkElement themeSource,
        string title,
        string content,
        string primaryButtonText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };
        DialogTheme.Apply(dialog, themeSource);

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
