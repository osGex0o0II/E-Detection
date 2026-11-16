using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EDetection.Desktop.Views;

internal static class DialogTheme
{
    public static void Apply(ContentDialog dialog, FrameworkElement themeSource)
    {
        // Dialogs live in the XamlRoot popup layer, so they follow the OS app
        // theme instead of the page theme unless the resolved theme is applied
        // explicitly.  ActualTheme is Light/Dark once the source is loaded.
        var theme = themeSource.ActualTheme;
        if (theme == ElementTheme.Default)
        {
            theme = Application.Current.RequestedTheme == ApplicationTheme.Dark
                ? ElementTheme.Dark
                : ElementTheme.Light;
        }

        dialog.RequestedTheme = theme;
    }
}
