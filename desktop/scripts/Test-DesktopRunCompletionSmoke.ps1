[CmdletBinding()]
param(
    [string]$PackagePath = "",

    [string]$AppPath = "",

    [string]$OutputDirectory = "",

    [int]$Width = 1600,

    [int]$Height = 1000,

    [int]$WaitSeconds = 20
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptDir "DesktopUiAutomation.ps1")
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")

if (![string]::IsNullOrWhiteSpace($PackagePath) -and [string]::IsNullOrWhiteSpace($AppPath)) {
    $packageFull = [System.IO.Path]::GetFullPath((Resolve-Path $PackagePath).Path)
    $AppPath = Join-Path $packageFull "EDetection.exe"
}

if ([string]::IsNullOrWhiteSpace($AppPath)) {
    $localAppPath = Join-Path $scriptDir "EDetection.exe"
    $AppPath = if (Test-Path $localAppPath) {
        $localAppPath
    }
    else {
        Join-Path $repoRoot "artifacts\desktop\win-x64\publish\EDetection.exe"
    }
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = if (Test-Path (Join-Path $scriptDir "EDetection.exe")) {
        Join-Path $scriptDir "smoke-results\run-completion"
    }
    else {
        Join-Path $repoRoot "artifacts\desktop\run-completion-smoke"
    }
}

$appFull = [System.IO.Path]::GetFullPath((Resolve-Path $AppPath).Path)
$outputFull = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputFull | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$sandboxDir = Join-Path $outputFull "sandbox-$timestamp"
$inputDir = Join-Path $sandboxDir "input"
New-Item -ItemType Directory -Force -Path $inputDir | Out-Null
Set-Content -Path (Join-Path $inputDir "demo.csv") -Value "time,Uab`n0,320`n" -Encoding UTF8

$settingsDirectory = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "E-Detection\Desktop"
$settingsPath = Join-Path $settingsDirectory "settings.json"
$settingsExisted = Test-Path $settingsPath
$settingsBackup = $null
if ($settingsExisted) {
    $settingsBackup = Get-Content -Path $settingsPath -Raw
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class DesktopRunCompletionSmokeNative
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);
}
"@

$process = $null

try {
    New-Item -ItemType Directory -Force -Path $settingsDirectory | Out-Null
    [pscustomobject]@{
        InputDirectory = $inputDir
        OutputDirectory = ""
        ConfigPath = "config.json"
        WriteReport = $false
        CloseToTrayOnClose = $false
        StartMinimizedToTray = $false
        AutoStartOnSignIn = $false
        EnableDesktopNotifications = $false
        SelectedThemeIndex = 0
        SelectedBackdropIndex = 0
        RecentReports = @()
    } | ConvertTo-Json | Set-Content -Path $settingsPath -Encoding UTF8

    $process = Start-Process -FilePath $appFull -WorkingDirectory (Split-Path -Parent $appFull) -PassThru
    $mainWindow = Wait-ForWindowTitle $process.Id "*E-Detection*" $WaitSeconds
    [DesktopRunCompletionSmokeNative]::ShowWindow($mainWindow.Handle, 9) | Out-Null

    $dpi = [DesktopRunCompletionSmokeNative]::GetDpiForWindow($mainWindow.Handle)
    if ($dpi -eq 0) {
        $dpi = 96
    }

    $scale = $dpi / 96.0
    $requestedPhysicalWidth = [Math]::Ceiling($Width * $scale)
    $requestedPhysicalHeight = [Math]::Ceiling($Height * $scale)
    $workArea = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $targetWidth = [Math]::Min($requestedPhysicalWidth, $workArea.Width)
    $targetHeight = [Math]::Min($requestedPhysicalHeight, $workArea.Height)
    [DesktopRunCompletionSmokeNative]::MoveWindow($mainWindow.Handle, $workArea.Left, $workArea.Top, $targetWidth, $targetHeight, $true) | Out-Null
    [DesktopRunCompletionSmokeNative]::SetForegroundWindow($mainWindow.Handle) | Out-Null
    Start-Sleep -Milliseconds 500

    $startAction = Invoke-AutomationElement $mainWindow.Handle "开始检测" 2
    if ($startAction -eq $null) {
        [System.Windows.Forms.SendKeys]::SendWait("{F5}")
    }

    $completionTitle = Wait-ForAutomationName $mainWindow.Handle "检测完成" $WaitSeconds
    $speedLabel = Wait-ForAutomationName $mainWindow.Handle "速度" $WaitSeconds
    $elapsedLabel = Wait-ForAutomationName $mainWindow.Handle "已用" $WaitSeconds
    $remainingLabel = Wait-ForAutomationName $mainWindow.Handle "剩余" $WaitSeconds
    $copySummaryAction = Wait-ForAutomationName $mainWindow.Handle "复制摘要" $WaitSeconds
    $openFirstAnomalyAction = Wait-ForAutomationName $mainWindow.Handle "定位首个异常" $WaitSeconds

    $resultPath = Join-Path $outputFull "run-completion-smoke-$timestamp.json"
    [pscustomobject]@{
        AppPath = $appFull
        ProcessId = $process.Id
        MainWindowTitle = $mainWindow.Title
        InputDirectory = $inputDir
        CompletionTitle = $completionTitle
        SpeedLabel = $speedLabel
        ElapsedLabel = $elapsedLabel
        RemainingLabel = $remainingLabel
        CopySummaryAction = $copySummaryAction
        OpenFirstAnomalyAction = $openFirstAnomalyAction
        Dpi = $dpi
        RequestedDipWidth = $Width
        RequestedDipHeight = $Height
        TargetWidth = $targetWidth
        TargetHeight = $targetHeight
        Trigger = "F5 with a one-file CSV input"
        Passed = $true
        CapturedAt = (Get-Date).ToString("o")
    } | ConvertTo-Json | Set-Content -Path $resultPath -Encoding UTF8

    Write-Host "Run-completion smoke passed: $resultPath"
}
finally {
    if ($process -ne $null -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        Start-Sleep -Seconds 2
        if (!$process.HasExited) {
            Stop-Process -Id $process.Id -Force
        }
    }

    if ($settingsExisted) {
        New-Item -ItemType Directory -Force -Path $settingsDirectory | Out-Null
        Set-Content -Path $settingsPath -Value $settingsBackup -Encoding UTF8
    }
    elseif (Test-Path $settingsPath) {
        Remove-Item -LiteralPath $settingsPath -Force
    }
}
