using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EDetection.Desktop.Views;

public sealed class AppTitleBar : TitleBar
{
    private ColumnDefinition? _rightPaddingColumn;

    public bool TrySetRightPaddingDip(double width)
    {
        if (_rightPaddingColumn is null)
        {
            return false;
        }

        if (_rightPaddingColumn.Width.IsAbsolute
            && Math.Abs(_rightPaddingColumn.Width.Value - width) < 0.5)
        {
            return false;
        }

        _rightPaddingColumn.Width = new GridLength(width);
        return true;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _rightPaddingColumn = GetTemplateChild("RightPaddingColumn") as ColumnDefinition;
    }
}
