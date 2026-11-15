using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDetection.Desktop.Models;
using EDetection.Desktop.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace EDetection.Desktop.ViewModels;

public partial class MainViewModel
    : ObservableObject,
        INetworkProxySettings,
        INtfyNotificationSettings,
        ILlmAssistantSettings
{

    public void PrepareForShellShutdown()
    {
        _isShellShutdownRequested = true;
        _cancelPromptCts?.Cancel();
        _runCts?.Cancel();

        if (_runStopwatch.IsRunning)
        {
            StopRunTelemetry();
        }

        if (IsRunning)
        {
            StatusText = "正在退出...";
            SetTaskbarProgress(TaskbarProgressKind.Paused, ProgressPercent);
        }

        SaveSettings();
    }

    public int WindowLeft { get; private set; } = -1;

    public int WindowTop { get; private set; } = -1;

    public int WindowWidth { get; private set; }

    public int WindowHeight { get; private set; }

    public bool IsWindowMaximized { get; private set; }
}
