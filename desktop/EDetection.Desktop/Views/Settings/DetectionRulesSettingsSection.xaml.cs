using EDetection.Desktop.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace EDetection.Desktop.Views.Settings;

public sealed partial class DetectionRulesSettingsSection : UserControl
{
    public DetectionRulesSettingsSection()
    {
        InitializeComponent();
    }

    public MainViewModel? ViewModel => DataContext as MainViewModel;
}
