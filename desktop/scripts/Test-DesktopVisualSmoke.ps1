[CmdletBinding()]
param(
    [string]$PackagePath = "",

    [string]$AppPath = "",

    [string]$OutputDirectory = "",

    [string]$SettingsDirectory = "",

    [int]$Width = 1600,

    [int]$Height = 1000,

    [int]$WaitSeconds = 5,

    [switch]$AllowClampedSize
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
        Join-Path $scriptDir "smoke-results\visual"
    }
    else {
        Join-Path $repoRoot "artifacts\desktop\visual-smoke"
    }
}

$appFull = [System.IO.Path]::GetFullPath((Resolve-Path $AppPath).Path)
$outputFull = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputFull | Out-Null

$settingsEnvironmentVariable = "EDETECTION_DESKTOP_SETTINGS_DIR"
$previousSettingsOverride = $env:EDETECTION_DESKTOP_SETTINGS_DIR
$settingsDirectoryFull = $null
if (![string]::IsNullOrWhiteSpace($SettingsDirectory)) {
    $settingsDirectoryFull = [System.IO.Path]::GetFullPath($SettingsDirectory)
    New-Item -ItemType Directory -Force -Path $settingsDirectoryFull | Out-Null
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class DesktopSmokeNative
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out RECT rect, int cbAttribute);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }
}
"@

# WinUI uses per-monitor DPI awareness.  A DPI-unaware test host sees the
# virtualized 1280x752 work area and MoveWindow silently scales the request,
# so requested DIP sizes would not be the sizes actually tested.
[DesktopSmokeNative]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null

$HWND_TOPMOST = [IntPtr](-1)
$HWND_NOTOPMOST = [IntPtr](-2)
$SWP_NOACTIVATE = 0x0010
$SWP_SHOWWINDOW = 0x0040
$DWMWA_EXTENDED_FRAME_BOUNDS = 9
$PW_RENDERFULLCONTENT = 0x00000002

function Get-MainWindowHandle([System.Diagnostics.Process]$Process, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Application exited early with code $($Process.ExitCode)"
        }

        if ($Process.MainWindowHandle -ne [IntPtr]::Zero) {
            return $Process.MainWindowHandle
        }

        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for the main window handle."
}

function Get-VisibleWindowRect([IntPtr]$Handle) {
    $rect = New-Object DesktopSmokeNative+RECT
    $result = [DesktopSmokeNative]::DwmGetWindowAttribute(
        $Handle,
        $DWMWA_EXTENDED_FRAME_BOUNDS,
        [ref]$rect,
        [System.Runtime.InteropServices.Marshal]::SizeOf([type][DesktopSmokeNative+RECT]))
    if ($result -eq 0) {
        return $rect
    }

    if (![DesktopSmokeNative]::GetWindowRect($Handle, [ref]$rect)) {
        throw "Failed to read window rectangle."
    }

    $rect
}

function Find-AutomationElementById([IntPtr]$RootHandle, [string]$AutomationId, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($RootHandle)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)

    do {
        $match = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($match -ne $null) {
            return $match
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for automation element with AutomationId '$AutomationId'."
}

function Test-ElementOnScreen(
    [System.Windows.Automation.AutomationElement]$Element,
    [DesktopSmokeNative+RECT]$ClientRect) {
    if ($Element -eq $null) {
        return $false
    }

    if ($Element.Current.IsOffscreen) {
        return $false
    }

    $bounds = $Element.Current.BoundingRectangle
    if ([double]::IsInfinity($bounds.X) -or [double]::IsInfinity($bounds.Y) -or [double]::IsInfinity($bounds.Width) -or [double]::IsInfinity($bounds.Height) -or $bounds.Width -le 0 -or $bounds.Height -le 0) {
        return $false
    }

    $left = [Math]::Max($bounds.Left, $ClientRect.Left)
    $top = [Math]::Max($bounds.Top, $ClientRect.Top)
    $right = [Math]::Min($bounds.Right, $ClientRect.Right)
    $bottom = [Math]::Min($bounds.Bottom, $ClientRect.Bottom)
    $visibleWidth = $right - $left
    $visibleHeight = $bottom - $top
    return $visibleWidth -ge [Math]::Min(8, $bounds.Width) -and $visibleHeight -ge [Math]::Min(8, $bounds.Height)
}

function Get-ElementBounds([System.Windows.Automation.AutomationElement]$Element) {
    if ($Element -eq $null) {
        return $null
    }

    $bounds = $Element.Current.BoundingRectangle
    [pscustomobject]@{
        Name = $Element.Current.Name
        AutomationId = $Element.Current.AutomationId
        ControlType = $Element.Current.ControlType.ProgrammaticName
        X = [Math]::Round($bounds.X, 1)
        Y = [Math]::Round($bounds.Y, 1)
        Width = [Math]::Round($bounds.Width, 1)
        Height = [Math]::Round($bounds.Height, 1)
        IsOffscreen = $Element.Current.IsOffscreen
    }
}

function Set-AutomationScrollPercent(
    [System.Windows.Automation.AutomationElement]$Element,
    [double]$VerticalPercent) {
    if ($Element -eq $null) {
        return $false
    }

    try {
        $pattern = [System.Windows.Automation.ScrollPattern]$Element.GetCurrentPattern(
            [System.Windows.Automation.ScrollPattern]::Pattern)
    }
    catch {
        return $false
    }

    if (!$pattern.Current.VerticallyScrollable) {
        return $false
    }

    $pattern.SetScrollPercent(
        [System.Windows.Automation.ScrollPattern]::NoScroll,
        $VerticalPercent)
    Start-Sleep -Milliseconds 600
    return $true
}

function Get-AutomationScrollState([System.Windows.Automation.AutomationElement]$Element) {
    if ($Element -eq $null) {
        return $null
    }

    try {
        $pattern = [System.Windows.Automation.ScrollPattern]$Element.GetCurrentPattern(
            [System.Windows.Automation.ScrollPattern]::Pattern)
    }
    catch {
        return $null
    }

    [pscustomobject]@{
        VerticallyScrollable = $pattern.Current.VerticallyScrollable
        HorizontalScrollable = $pattern.Current.HorizontallyScrollable
        VerticalViewSize = [Math]::Round($pattern.Current.VerticalViewSize, 2)
        VerticalScrollPercent = [Math]::Round($pattern.Current.VerticalScrollPercent, 2)
    }
}

function Bring-AutomationElementIntoView([System.Windows.Automation.AutomationElement]$Element) {
    if ($Element -eq $null) {
        return $false
    }

    try {
        $scrollItem = [System.Windows.Automation.ScrollItemPattern]$Element.GetCurrentPattern(
            [System.Windows.Automation.ScrollItemPattern]::Pattern)
        $scrollItem.ScrollIntoView()
    }
    catch {
    }

    try {
        $Element.SetFocus()
    }
    catch {
    }

    Start-Sleep -Milliseconds 500
    return $true
}

function Test-BitmapIsUseful([System.Drawing.Bitmap]$Bitmap) {
    $samples = 0
    $unique = New-Object 'System.Collections.Generic.HashSet[int]'
    $stepX = [Math]::Max(1, [Math]::Floor($Bitmap.Width / 64))
    $stepY = [Math]::Max(1, [Math]::Floor($Bitmap.Height / 64))

    for ($y = 0; $y -lt $Bitmap.Height; $y += $stepY) {
        for ($x = 0; $x -lt $Bitmap.Width; $x += $stepX) {
            [void]$unique.Add($Bitmap.GetPixel($x, $y).ToArgb())
            $samples += 1
        }
    }

    [pscustomobject]@{
        Samples = $samples
        UniqueColors = $unique.Count
        IsUseful = $Bitmap.Width -ge 900 -and $Bitmap.Height -ge 620 -and $unique.Count -gt 12
    }
}

function Capture-WindowBitmap([IntPtr]$Handle, [DesktopSmokeNative+RECT]$Rect) {
    $captureWidth = $Rect.Right - $Rect.Left
    $captureHeight = $Rect.Bottom - $Rect.Top
    if ($captureWidth -le 0 -or $captureHeight -le 0) {
        throw "Invalid window rectangle: $captureWidth x $captureHeight"
    }

    $bufferBitmap = New-Object System.Drawing.Bitmap $captureWidth, $captureHeight
    $bitmap = $null
    $windowDc = [DesktopSmokeNative]::GetWindowDC($Handle)
    $memoryDc = [IntPtr]::Zero
    $hBitmap = [IntPtr]::Zero
    $oldObject = [IntPtr]::Zero

    try {
        if ($windowDc -eq [IntPtr]::Zero) {
            throw "GetWindowDC returned null."
        }

        $memoryDc = [DesktopSmokeNative]::CreateCompatibleDC($windowDc)
        if ($memoryDc -eq [IntPtr]::Zero) {
            throw "CreateCompatibleDC returned null."
        }

        $hBitmap = $bufferBitmap.GetHbitmap()
        $oldObject = [DesktopSmokeNative]::SelectObject($memoryDc, $hBitmap)
        $printed = [DesktopSmokeNative]::PrintWindow($Handle, $memoryDc, $PW_RENDERFULLCONTENT)
        if (!$printed) {
            $printed = [DesktopSmokeNative]::PrintWindow($Handle, $memoryDc, 0)
        }

        if (!$printed) {
            $bitmap = $bufferBitmap
            $bufferBitmap = $null
            return [pscustomobject]@{
                Bitmap = $bitmap
                Method = "PrintWindowFailed"
            }
        }

        $bitmap = [System.Drawing.Image]::FromHbitmap($hBitmap)
    }
    finally {
        if ($oldObject -ne [IntPtr]::Zero -and $memoryDc -ne [IntPtr]::Zero) {
            [DesktopSmokeNative]::SelectObject($memoryDc, $oldObject) | Out-Null
        }

        if ($hBitmap -ne [IntPtr]::Zero) {
            [DesktopSmokeNative]::DeleteObject($hBitmap) | Out-Null
        }

        if ($memoryDc -ne [IntPtr]::Zero) {
            [DesktopSmokeNative]::DeleteDC($memoryDc) | Out-Null
        }

        if ($windowDc -ne [IntPtr]::Zero) {
            [DesktopSmokeNative]::ReleaseDC($Handle, $windowDc) | Out-Null
        }

        if ($bufferBitmap -ne $null) {
            $bufferBitmap.Dispose()
        }
    }

    [pscustomobject]@{
        Bitmap = $bitmap
        Method = "PrintWindow"
    }
}

function Capture-ScreenBitmap([DesktopSmokeNative+RECT]$Rect) {
    $captureWidth = $Rect.Right - $Rect.Left
    $captureHeight = $Rect.Bottom - $Rect.Top
    if ($captureWidth -le 0 -or $captureHeight -le 0) {
        throw "Invalid window rectangle: $captureWidth x $captureHeight"
    }

    $bitmap = New-Object System.Drawing.Bitmap $captureWidth, $captureHeight
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($Rect.Left, $Rect.Top, 0, 0, $bitmap.Size)
    $graphics.Dispose()
    [pscustomobject]@{
        Bitmap = $bitmap
        Method = "CopyFromScreen"
    }
}

$process = $null
$handle = [IntPtr]::Zero
$bitmap = $null
$graphics = $null

if ($settingsDirectoryFull -ne $null) {
    $env:EDETECTION_DESKTOP_SETTINGS_DIR = $settingsDirectoryFull
}
else {
    Remove-Item -Path "Env:$settingsEnvironmentVariable" -ErrorAction SilentlyContinue
}

try {
    $process = Start-Process -FilePath $appFull -WorkingDirectory (Split-Path -Parent $appFull) -PassThru
    $handle = Get-MainWindowHandle $process $WaitSeconds
    $windowTitle = Get-WindowTitle $handle
    if ($windowTitle -notlike "*E-Detection*") {
        throw "Unexpected main window title '$windowTitle'. Expected an E-Detection window."
    }

    $dpi = [DesktopSmokeNative]::GetDpiForWindow($handle)
    if ($dpi -eq 0) {
        $dpi = 96
    }

    $scale = $dpi / 96.0
    $workArea = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $targetX = $workArea.Left
    $targetY = $workArea.Top

    # Measure the non-client frame before resizing so the requested size is a
    # client-size request.  MoveWindow sizes the outer window; without this the
    # reported client area is always smaller than the request by the frame.
    $initialWindowRect = New-Object DesktopSmokeNative+RECT
    if (![DesktopSmokeNative]::GetWindowRect($handle, [ref]$initialWindowRect)) {
        throw "Failed to read the initial window rectangle."
    }

    $initialClientRect = New-Object DesktopSmokeNative+RECT
    if (![DesktopSmokeNative]::GetClientRect($handle, [ref]$initialClientRect)) {
        throw "Failed to read the initial client rectangle."
    }

    $frameWidthPhysical = ($initialWindowRect.Right - $initialWindowRect.Left) - ($initialClientRect.Right - $initialClientRect.Left)
    $frameHeightPhysical = ($initialWindowRect.Bottom - $initialWindowRect.Top) - ($initialClientRect.Bottom - $initialClientRect.Top)
    $requestedClientWidthPhysical = [Math]::Ceiling($Width * $scale)
    $requestedClientHeightPhysical = [Math]::Ceiling($Height * $scale)
    $desiredOuterWidth = $requestedClientWidthPhysical + [Math]::Max(0, $frameWidthPhysical)
    $desiredOuterHeight = $requestedClientHeightPhysical + [Math]::Max(0, $frameHeightPhysical)
    $targetWidth = [Math]::Min($desiredOuterWidth, $workArea.Width)
    $targetHeight = [Math]::Min($desiredOuterHeight, $workArea.Height)

    [DesktopSmokeNative]::ShowWindow($handle, 9) | Out-Null
    [DesktopSmokeNative]::MoveWindow($handle, $targetX, $targetY, $targetWidth, $targetHeight, $true) | Out-Null
    [DesktopSmokeNative]::SetWindowPos($handle, $HWND_TOPMOST, $targetX, $targetY, $targetWidth, $targetHeight, $SWP_SHOWWINDOW) | Out-Null
    [DesktopSmokeNative]::SetForegroundWindow($handle) | Out-Null
    Start-Sleep -Seconds 2
    $foreground = [DesktopSmokeNative]::GetForegroundWindow()
    $foregroundTitle = if ($foreground -eq [IntPtr]::Zero) { "" } else { Get-WindowTitle $foreground }

    $startAction = Wait-ForAutomationName $handle "开始检测" $WaitSeconds
    $settingsAction = Wait-ForAutomationName $handle "设置" $WaitSeconds
    $eventLogTab = Wait-ForAutomationName $handle "运行记录" $WaitSeconds

    $startElement = Find-AutomationElementById $handle "RunStartButton" $WaitSeconds
    $settingsElement = Find-AutomationElementById $handle "SettingsTitleBarButton" $WaitSeconds
    $processedMetricElement = Wait-ForAutomationElement $handle "文件" $WaitSeconds
    $metricElement = Wait-ForAutomationElement $handle "异常文件" $WaitSeconds
    $anomalyRecordsMetricElement = Wait-ForAutomationElement $handle "异常记录" $WaitSeconds
    $skippedMetricElement = Wait-ForAutomationElement $handle "跳过" $WaitSeconds
    $highRiskElement = Wait-ForAutomationElement $handle "高风险设备" $WaitSeconds
    $issueRankElement = Wait-ForAutomationElement $handle "异常类型排行" $WaitSeconds
    $detailSearchElement = Wait-ForAutomationElement $handle "搜索异常明细" $WaitSeconds
    $severityFilterElement = Wait-ForAutomationElement $handle "筛选异常等级" $WaitSeconds
    $issueTypeFilterElement = Wait-ForAutomationElement $handle "筛选异常类型" $WaitSeconds
    $reportHistorySearchElement = Find-AutomationElementById $handle "ReportHistorySearchBox" $WaitSeconds
    $reportHistoryFilterElement = Find-AutomationElementById $handle "ReportHistoryFilterComboBox" $WaitSeconds
    $detailTableElement = Find-AutomationElementById $handle "DetailTable" $WaitSeconds
    $accessibleNamesOk = $reportHistorySearchElement.Current.Name -eq "搜索报告历史" `
        -and $reportHistoryFilterElement.Current.Name -eq "筛选报告历史" `
        -and $detailTableElement.Current.Name -eq "异常明细表"
    $setupScrollElement = Find-AutomationElementById $handle "SetupScroll" $WaitSeconds
    $workbenchScrollElement = Find-AutomationElementById $handle "WorkbenchScroll" $WaitSeconds

    $rect = Get-VisibleWindowRect $handle
    $clientRect = New-Object DesktopSmokeNative+RECT
    if (![DesktopSmokeNative]::GetClientRect($handle, [ref]$clientRect)) {
        throw "Failed to read the client rectangle."
    }

    $clientOrigin = New-Object DesktopSmokeNative+POINT
    if (![DesktopSmokeNative]::ClientToScreen($handle, [ref]$clientOrigin)) {
        throw "Failed to map the client rectangle to screen coordinates."
    }

    $clientScreenRect = New-Object DesktopSmokeNative+RECT
    $clientScreenRect.Left = $clientOrigin.X
    $clientScreenRect.Top = $clientOrigin.Y
    $clientScreenRect.Right = $clientOrigin.X + $clientRect.Right
    $clientScreenRect.Bottom = $clientOrigin.Y + $clientRect.Bottom

    $actualOuterWidthDip = ($rect.Right - $rect.Left) / $scale
    $actualOuterHeightDip = ($rect.Bottom - $rect.Top) / $scale
    $actualClientWidthDip = ($clientRect.Right - $clientRect.Left) / $scale
    $actualClientHeightDip = ($clientRect.Bottom - $clientRect.Top) / $scale
    $sizeToleranceDip = 2
    $widthClampedByWorkArea = $desiredOuterWidth -gt $workArea.Width
    $heightClampedByWorkArea = $desiredOuterHeight -gt $workArea.Height
    $widthClampedByMinimum = $actualClientWidthDip -gt $Width + $sizeToleranceDip
    $widthClampedByResize = $actualClientWidthDip -lt $Width - $sizeToleranceDip
    $heightClampedByMinimum = $actualClientHeightDip -gt $Height + $sizeToleranceDip
    $heightClampedByResize = $actualClientHeightDip -lt $Height - $sizeToleranceDip

    $clampReasons = [System.Collections.Generic.List[string]]::new()
    if ($widthClampedByWorkArea) { $clampReasons.Add("requested width exceeds the work area") }
    if ($heightClampedByWorkArea) { $clampReasons.Add("requested height exceeds the work area") }
    if ($widthClampedByMinimum) { $clampReasons.Add("window minimum width is larger than requested") }
    if ($widthClampedByResize) { $clampReasons.Add("window manager reduced the requested width") }
    if ($heightClampedByMinimum) { $clampReasons.Add("window minimum height is larger than requested") }
    if ($heightClampedByResize) { $clampReasons.Add("window manager reduced the requested height") }
    $sizeClamped = $clampReasons.Count -gt 0

    $startVisible = Test-ElementOnScreen $startElement $clientScreenRect
    $startEnabled = $startElement.Current.IsEnabled
    $startClickable = $false
    if ($startEnabled) {
        try {
            $startInvokePattern = [System.Windows.Automation.InvokePattern]$startElement.GetCurrentPattern(
                [System.Windows.Automation.InvokePattern]::Pattern)
            $startClickable = $null -ne $startInvokePattern
        }
        catch {
            $startClickable = $false
        }
    }

    $settingsVisible = Test-ElementOnScreen $settingsElement $clientScreenRect
    $settingsBounds = Get-ElementBounds $settingsElement
    if ($settingsBounds.ControlType -ne "ControlType.Button") {
        throw "Visual smoke failed: expected the Settings button, found $($settingsBounds.ControlType)."
    }

    $minimizeElement = Find-AutomationElementById $handle "Minimize" $WaitSeconds
    $minimizeBounds = Get-ElementBounds $minimizeElement
    $settingsCaptionGapDip = (($minimizeBounds.X - ($settingsBounds.X + $settingsBounds.Width)) / $scale)
    $settingsCaptionTopOffsetDip = (($settingsBounds.Y - $minimizeBounds.Y) / $scale)
    $settingsCaptionAligned = $settingsCaptionGapDip -ge -2 `
        -and $settingsCaptionGapDip -le 6 `
        -and [Math]::Abs($settingsCaptionTopOffsetDip) -le 2
    $settingsButtonWidthDip = $settingsBounds.Width / $scale
    $settingsLabelExpected = $actualClientWidthDip -ge 1080
    $settingsLabelVisibleByWidth = if ($settingsLabelExpected) {
        $settingsButtonWidthDip -ge 60
    }
    else {
        $settingsButtonWidthDip -le 52
    }

    $rootAutomationElement = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    $logoNameCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        "E-Detection")
    $logoMatches = $rootAutomationElement.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        $logoNameCondition)
    $logoGroupCount = 0
    $logoTextCount = 0
    foreach ($logoMatch in $logoMatches) {
        if ($logoMatch.Current.ControlType -eq [System.Windows.Automation.ControlType]::Group) {
            $logoGroupCount++
        }
        elseif ($logoMatch.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) {
            $logoTextCount++
        }
    }

    $settingsInCaptionRegion = $settingsVisible -and (($settingsBounds.X - $clientScreenRect.Left) -ge ($clientScreenRect.Right - $clientScreenRect.Left) * 0.70)
    $metricElements = @(
        $processedMetricElement,
        $metricElement,
        $anomalyRecordsMetricElement,
        $skippedMetricElement
    )
    $metricBoundsList = @($metricElements | ForEach-Object { Get-ElementBounds $_ })
    $metricVisible = $true
    foreach ($element in $metricElements) {
        if (!(Test-ElementOnScreen $element $clientScreenRect)) {
            $metricVisible = $false
            break
        }
    }

    $metricYValues = @($metricBoundsList | ForEach-Object { $_.Y })
    $metricYMaximum = ($metricYValues | Measure-Object -Maximum).Maximum
    $metricYMinimum = ($metricYValues | Measure-Object -Minimum).Minimum
    $metricYSpread = $metricYMaximum - $metricYMinimum
    $metricLayout = if ($metricYSpread -le (12 * $scale)) { "4-across" } else { "2x2" }
    # WorkbenchScroll carries the workbench content; DetectionWorkbenchView
    # adds a 20 DIP right margin for the vertical scrollbar gutter.
    $workbenchBounds = Get-ElementBounds $workbenchScrollElement
    $workbenchWidthDip = ($workbenchBounds.Width / $scale) - 20
    $expectedMetricLayout = if ($workbenchWidthDip -ge 700) { "4-across" } else { "2x2" }
    $metricLayoutMatches = $metricLayout -eq $expectedMetricLayout
    $workbenchPaneVisible = Test-ElementOnScreen $workbenchScrollElement $clientScreenRect

    $highRiskVisible = Test-ElementOnScreen $highRiskElement $clientScreenRect
    $highRiskBounds = Get-ElementBounds $highRiskElement
    $issueRankBounds = Get-ElementBounds $issueRankElement
    $riskLayout = if ([Math]::Abs($highRiskBounds.Y - $issueRankBounds.Y) -le (20 * $scale)) { "side-by-side" } else { "stacked" }
    $expectedRiskLayout = if ($workbenchWidthDip -ge 900) { "side-by-side" } else { "stacked" }
    $riskLayoutMatches = $riskLayout -eq $expectedRiskLayout

    $setupScrollState = Get-AutomationScrollState $setupScrollElement
    if ($setupScrollState -ne $null -and $setupScrollState.VerticallyScrollable) {
        [void](Set-AutomationScrollPercent $setupScrollElement 100)
    }

    $clearHistoryElement = Find-AutomationElementById $handle "ClearRecentReportsButton" $WaitSeconds
    if (!(Test-ElementOnScreen $clearHistoryElement $clientScreenRect)) {
        [void](Bring-AutomationElementIntoView $clearHistoryElement)
    }

    $setupBottomReachable = Test-ElementOnScreen $clearHistoryElement $clientScreenRect

    $reportHistoryEmptyCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        "ReportHistoryEmptyState")
    $reportHistoryEmptyElement = $rootAutomationElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $reportHistoryEmptyCondition)
    $reportHistoryEmptyRequired = $settingsDirectoryFull -ne $null
    $reportHistoryEmptyVisible = $reportHistoryEmptyElement -ne $null `
        -and (Test-ElementOnScreen $reportHistoryEmptyElement $clientScreenRect)
    $reportHistoryEmptyStateOk = -not $reportHistoryEmptyRequired -or $reportHistoryEmptyVisible

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $setupBottomScreenshotPath = Join-Path $outputFull "main-setup-bottom-$timestamp.png"
    $setupCapture = Capture-WindowBitmap $handle $rect
    $setupBitmap = $setupCapture.Bitmap
    $setupBitmap.Save($setupBottomScreenshotPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $setupBitmap.Dispose()
    $settingsElement.SetFocus()
    Start-Sleep -Milliseconds 200
    if ($setupScrollState -ne $null -and $setupScrollState.VerticallyScrollable) {
        [void](Set-AutomationScrollPercent $setupScrollElement 0)
    }

    $workbenchScrollState = Get-AutomationScrollState $workbenchScrollElement
    $workbenchScrollable = $workbenchScrollState -ne $null -and $workbenchScrollState.VerticallyScrollable
    if ($workbenchScrollable) {
        [void](Set-AutomationScrollPercent $workbenchScrollElement 100)
        Start-Sleep -Milliseconds 600
    }

    # The detail filters live below the initial workbench fold. Bring them into
    # view before reading bounds so UIA reports real rectangles for every field.
    [void](Bring-AutomationElementIntoView $detailSearchElement)
    Start-Sleep -Milliseconds 500
    $detailSearchBounds = Get-ElementBounds $detailSearchElement
    $severityFilterBounds = Get-ElementBounds $severityFilterElement
    $issueTypeFilterBounds = Get-ElementBounds $issueTypeFilterElement
    $filterYValues = @($detailSearchBounds.Y, $severityFilterBounds.Y, $issueTypeFilterBounds.Y)
    $filterYMaximum = ($filterYValues | Measure-Object -Maximum).Maximum
    $filterYMinimum = ($filterYValues | Measure-Object -Minimum).Minimum
    $filterYSpread = $filterYMaximum - $filterYMinimum
    $filterLayout = if ($filterYSpread -le (12 * $scale)) { "one-row" } else { "wrapped" }
    $expectedFilterLayout = if ($workbenchWidthDip -ge 720) { "one-row" } else { "wrapped" }
    $filterLayoutMatches = $filterLayout -eq $expectedFilterLayout

    $noDetailCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        "暂无异常明细")
    $noDetailElement = $rootAutomationElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $noDetailCondition)
    $detailEmptyCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        "DetailEmptyState")
    $detailEmptyElement = $rootAutomationElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $detailEmptyCondition)
    $detailEmptyExpected = $noDetailElement -ne $null
    $detailEmptyVisible = $detailEmptyElement -ne $null `
        -and (Test-ElementOnScreen $detailEmptyElement $clientScreenRect)
    $detailEmptyStateOk = -not $detailEmptyExpected -or $detailEmptyVisible

    $sortHeaderElement = Wait-ForAutomationElement $handle "按等级排序" $WaitSeconds
    [void](Bring-AutomationElementIntoView $sortHeaderElement)
    $sortHeaderElement.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 300
    $sortAscending = (Find-AutomationElementById $handle "DetailSortStatus" $WaitSeconds).Current.Name -eq "按等级升序"
    $sortHeaderElement.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 300
    $sortDescending = (Find-AutomationElementById $handle "DetailSortStatus" $WaitSeconds).Current.Name -eq "按等级降序"
    $sortHeaderElement.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 300
    $sortDefault = (Find-AutomationElementById $handle "DetailSortStatus" $WaitSeconds).Current.Name -eq "默认顺序"
    $sortStateOk = $sortAscending -and $sortDescending -and $sortDefault

    $eventLogElement = Wait-ForAutomationElement $handle "运行记录" $WaitSeconds
    if (!(Test-ElementOnScreen $eventLogElement $clientScreenRect)) {
        [void](Bring-AutomationElementIntoView $eventLogElement)
    }

    $workbenchTabReachable = Test-ElementOnScreen $eventLogElement $clientScreenRect

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $workbenchBottomScreenshotPath = Join-Path $outputFull "main-workbench-bottom-$timestamp.png"
    $bottomCapture = Capture-WindowBitmap $handle $rect
    $bottomBitmap = $bottomCapture.Bitmap
    $bottomBitmap.Save($workbenchBottomScreenshotPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bottomBitmap.Dispose()

    $settingsElement.SetFocus()
    Start-Sleep -Milliseconds 200
    if ($workbenchScrollable) {
        [void](Set-AutomationScrollPercent $workbenchScrollElement 0)
    }
    if ($setupScrollState -ne $null -and $setupScrollState.VerticallyScrollable) {
        [void](Set-AutomationScrollPercent $setupScrollElement 0)
    }
    Start-Sleep -Milliseconds 300

    $capture = Capture-WindowBitmap $handle $rect
    $bitmap = $capture.Bitmap
    $analysis = Test-BitmapIsUseful $bitmap
    if (!$analysis.IsUseful) {
        $bitmap.Dispose()
        $capture = Capture-ScreenBitmap $rect
        $bitmap = $capture.Bitmap
        $analysis = Test-BitmapIsUseful $bitmap
    }

    $screenshotPath = Join-Path $outputFull "main-window-$timestamp.png"
    $bitmap.Save($screenshotPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $layoutChecksPassed = $analysis.IsUseful `
        -and $settingsInCaptionRegion `
        -and $settingsCaptionAligned `
        -and $settingsLabelVisibleByWidth `
        -and ($logoGroupCount -eq 0) `
        -and ($logoTextCount -ge 1) `
        -and $accessibleNamesOk `
        -and $startVisible `
        -and $startClickable `
        -and $metricVisible `
        -and $workbenchPaneVisible `
        -and $metricLayoutMatches `
        -and $riskLayoutMatches `
        -and $filterLayoutMatches `
        -and $detailEmptyStateOk `
        -and $reportHistoryEmptyStateOk `
        -and $sortStateOk `
        -and $setupBottomReachable `
        -and $workbenchTabReachable
    $runStatus = if ($sizeClamped) { "Skipped-ClampedRequestedSize" } elseif ($layoutChecksPassed) { "Passed" } else { "Failed" }

    $result = [pscustomobject]@{
        AppPath = $appFull
        ProcessId = $process.Id
        WindowTitle = $windowTitle
        ForegroundTitle = $foregroundTitle
        WasForeground = ($foreground -eq $handle)
        CaptureMethod = $capture.Method
        ScreenshotPath = $screenshotPath
        SetupBottomScreenshotPath = $setupBottomScreenshotPath
        WorkbenchBottomScreenshotPath = $workbenchBottomScreenshotPath
        Dpi = $dpi
        RequestedDipWidth = $Width
        RequestedDipHeight = $Height
        TargetWidth = $targetWidth
        TargetHeight = $targetHeight
        ActualOuterWidthDip = [Math]::Round($actualOuterWidthDip, 1)
        ActualOuterHeightDip = [Math]::Round($actualOuterHeightDip, 1)
        ActualClientWidthDip = [Math]::Round($actualClientWidthDip, 1)
        ActualClientHeightDip = [Math]::Round($actualClientHeightDip, 1)
        RequestedSizeClamped = $sizeClamped
        RequestedSizeVerified = -not $sizeClamped
        ClampReasons = @($clampReasons)
        EffectiveTestedDipWidth = [Math]::Round($actualClientWidthDip, 1)
        EffectiveTestedDipHeight = [Math]::Round($actualClientHeightDip, 1)
        Width = $bitmap.Width
        Height = $bitmap.Height
        StartAction = $startAction
        SettingsAction = $settingsAction
        EventLogTab = $eventLogTab
        StartVisible = $startVisible
        StartEnabled = $startEnabled
        StartClickable = $startClickable
        SettingsVisible = $settingsVisible
        SettingsInCaptionRegion = $settingsInCaptionRegion
        SettingsButtonBounds = $settingsBounds
        SettingsButtonWidthDip = [Math]::Round($settingsButtonWidthDip, 1)
        SettingsToCaptionGapDip = [Math]::Round($settingsCaptionGapDip, 1)
        SettingsCaptionTopOffsetDip = [Math]::Round($settingsCaptionTopOffsetDip, 1)
        SettingsCaptionAligned = $settingsCaptionAligned
        SettingsLabelExpected = $settingsLabelExpected
        SettingsLabelVisibleByWidth = $settingsLabelVisibleByWidth
        LogoNameGroupCount = $logoGroupCount
        LogoNameTextCount = $logoTextCount
        ReportHistoryEmptyStateRequired = $reportHistoryEmptyRequired
        ReportHistoryEmptyStateVisible = $reportHistoryEmptyVisible
        ReportHistoryEmptyStateOk = $reportHistoryEmptyStateOk
        DetailEmptyStateExpected = $detailEmptyExpected
        DetailEmptyStateVisible = $detailEmptyVisible
        DetailEmptyStateOk = $detailEmptyStateOk
        AccessibleNamesOk = $accessibleNamesOk
        SortAscending = $sortAscending
        SortDescending = $sortDescending
        SortDefault = $sortDefault
        SortStateOk = $sortStateOk
        WorkbenchWidthDip = [Math]::Round($workbenchWidthDip, 1)
        WorkbenchPaneVisible = $workbenchPaneVisible
        MetricVisible = $metricVisible
        MetricLayout = $metricLayout
        ExpectedMetricLayout = $expectedMetricLayout
        MetricLayoutMatches = $metricLayoutMatches
        RiskLayout = $riskLayout
        ExpectedRiskLayout = $expectedRiskLayout
        RiskLayoutMatches = $riskLayoutMatches
        FilterLayout = $filterLayout
        ExpectedFilterLayout = $expectedFilterLayout
        FilterLayoutMatches = $filterLayoutMatches
        DetailSearchBounds = $detailSearchBounds
        SeverityFilterBounds = $severityFilterBounds
        IssueTypeFilterBounds = $issueTypeFilterBounds
        HighRiskVisible = $highRiskVisible
        SetupScroll = $setupScrollState
        SetupBottomReachable = $setupBottomReachable
        WorkbenchScroll = $workbenchScrollState
        WorkbenchTabReachable = $workbenchTabReachable
        StartButtonBounds = Get-ElementBounds $startElement
        MetricBounds = Get-ElementBounds $metricElement
        MetricBoundsAll = @($metricBoundsList)
        HighRiskBounds = $highRiskBounds
        IssueRankBounds = $issueRankBounds
        EventLogTabBounds = Get-ElementBounds $eventLogElement
        Samples = $analysis.Samples
        UniqueColors = $analysis.UniqueColors
        Status = $runStatus
        PassedAtActualSize = $layoutChecksPassed
        Passed = (-not $sizeClamped) -and $layoutChecksPassed
        CapturedAt = (Get-Date).ToString("o")
    }

    $resultPath = Join-Path $outputFull "visual-smoke-$timestamp.json"
    $result | ConvertTo-Json -Depth 6 | Set-Content -Path $resultPath -Encoding UTF8

    if (!$analysis.IsUseful) {
        throw "Visual smoke failed: screenshot appears blank or undersized. See $screenshotPath"
    }

    if (!$startVisible -or !$startClickable) {
        throw "Visual smoke failed: the primary Start button is not initially visible and clickable at $([Math]::Round($actualClientWidthDip, 1))x$([Math]::Round($actualClientHeightDip, 1)) DIP (visible=$startVisible, enabled=$startEnabled, clickable=$startClickable). See $screenshotPath"
    }

    if (!$workbenchPaneVisible -or !$metricVisible) {
        throw "Visual smoke failed: the workbench pane or its metric cards are not on-screen at the actual size ($([Math]::Round($actualClientWidthDip, 1))x$([Math]::Round($actualClientHeightDip, 1)) DIP). See $screenshotPath"
    }

    if (!$metricLayoutMatches) {
        throw "Visual smoke failed: workbench metrics use '$metricLayout' at $([Math]::Round($workbenchWidthDip, 1)) DIP width, expected '$expectedMetricLayout'."
    }

    if (!$riskLayoutMatches) {
        throw "Visual smoke failed: the risk panels use '$riskLayout' at $([Math]::Round($workbenchWidthDip, 1)) DIP width, expected '$expectedRiskLayout'."
    }

    if (!$filterLayoutMatches) {
        throw "Visual smoke failed: detail filters use '$filterLayout' at $([Math]::Round($workbenchWidthDip, 1)) DIP width, expected '$expectedFilterLayout'."
    }

    if (!$settingsInCaptionRegion) {
        throw "Visual smoke failed: the title-bar Settings button is not aligned with the caption-button region (X=$($settingsBounds.X), client width=$($clientScreenRect.Right - $clientScreenRect.Left))."
    }

    if (!$settingsCaptionAligned) {
        throw "Visual smoke failed: the title-bar Settings button is not in the caption band (gap=$([Math]::Round($settingsCaptionGapDip, 1)) DIP, top offset=$([Math]::Round($settingsCaptionTopOffsetDip, 1)) DIP)."
    }

    if (!$settingsLabelVisibleByWidth) {
        throw "Visual smoke failed: the title-bar Settings label width ($([Math]::Round($settingsButtonWidthDip, 1)) DIP) does not match the expected $($settingsLabelExpected ? 'visible' : 'compact') layout."
    }

    if ($logoGroupCount -ne 0 -or $logoTextCount -lt 1) {
        throw "Visual smoke failed: the title-bar logo exposes a duplicate 'E-Detection' automation name (groups=$logoGroupCount, texts=$logoTextCount)."
    }

    if (!$reportHistoryEmptyStateOk) {
        throw "Visual smoke failed: the empty report history state is not visible when no reports exist."
    }

    if (!$detailEmptyStateOk) {
        throw "Visual smoke failed: the empty anomaly detail state is not visible when no details exist."
    }

    if (!$accessibleNamesOk) {
        throw "Visual smoke failed: the report-history search/filter or anomaly detail table is missing its accessible name."
    }

    if (!$sortStateOk) {
        throw "Visual smoke failed: the anomaly detail sort state did not cycle through ascending, descending and default."
    }

    if (!$setupBottomReachable) {
        throw "Visual smoke failed: setup content is not reachable. See $screenshotPath"
    }

    if (!$workbenchTabReachable) {
        throw "Visual smoke failed: the workbench tabs are not reachable. See $workbenchBottomScreenshotPath"
    }

    if ($sizeClamped) {
        $clampDetail = "Requested $($Width)x$($Height) DIP produced a $([Math]::Round($actualClientWidthDip, 1))x$([Math]::Round($actualClientHeightDip, 1)) DIP client area ($($clampReasons -join '; '))."
        if (!$AllowClampedSize) {
            throw "Visual smoke invalid for the requested size: $clampDetail Re-run with -AllowClampedSize only for clamped-size layout evidence."
        }

        Write-Warning "SKIPPED requested-size verification: $clampDetail Layout assertions passed at the clamped size only."
        Write-Host "Visual smoke clamped-size evidence captured at $([Math]::Round($actualClientWidthDip, 1))x$([Math]::Round($actualClientHeightDip, 1)) DIP client: $screenshotPath"
        Write-Host "Result: $resultPath"
        return
    }

    Write-Host "Visual smoke passed at $([Math]::Round($actualClientWidthDip, 1))x$([Math]::Round($actualClientHeightDip, 1)) DIP client: $screenshotPath"
    Write-Host "Result: $resultPath"
}
finally {
    if ($process -ne $null -and !$process.HasExited -and $handle -ne [IntPtr]::Zero) {
        [DesktopSmokeNative]::SetWindowPos($handle, $HWND_NOTOPMOST, 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor $SWP_NOACTIVATE) | Out-Null
    }

    if ($bitmap -ne $null) {
        $bitmap.Dispose()
    }

    if ($process -ne $null -and !$process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        Start-Sleep -Seconds 2
        if (!$process.HasExited) {
            Stop-Process -Id $process.Id -Force
        }
    }

    if ($null -eq $previousSettingsOverride) {
        Remove-Item -Path "Env:$settingsEnvironmentVariable" -ErrorAction SilentlyContinue
    }
    else {
        $env:EDETECTION_DESKTOP_SETTINGS_DIR = $previousSettingsOverride
    }
}
