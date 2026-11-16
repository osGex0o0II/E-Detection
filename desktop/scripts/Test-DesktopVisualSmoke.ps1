[CmdletBinding()]
param(
    [string]$PackagePath = "",

    [string]$AppPath = "",

    [string]$OutputDirectory = "",

    [int]$Width = 1600,

    [int]$Height = 1000,

    [int]$WaitSeconds = 5
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
    $requestedPhysicalWidth = [Math]::Ceiling($Width * $scale)
    $requestedPhysicalHeight = [Math]::Ceiling($Height * $scale)
    $workArea = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $targetX = $workArea.Left
    $targetY = $workArea.Top
    $targetWidth = [Math]::Min($requestedPhysicalWidth, $workArea.Width)
    $targetHeight = [Math]::Min($requestedPhysicalHeight, $workArea.Height)

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
    $metricElement = Wait-ForAutomationElement $handle "异常文件" $WaitSeconds
    $highRiskElement = Wait-ForAutomationElement $handle "高风险设备" $WaitSeconds
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
    $sizeToleranceDip = 24
    $widthClampedByWorkArea = $requestedPhysicalWidth -gt $workArea.Width
    $heightClampedByWorkArea = $requestedPhysicalHeight -gt $workArea.Height
    $widthClampedByMinimum = $actualOuterWidthDip -gt $Width + $sizeToleranceDip
    $widthClampedByResize = $actualOuterWidthDip -lt $Width - $sizeToleranceDip
    $heightClampedByMinimum = $actualOuterHeightDip -gt $Height + $sizeToleranceDip
    $heightClampedByResize = $actualOuterHeightDip -lt $Height - $sizeToleranceDip

    $clampReasons = [System.Collections.Generic.List[string]]::new()
    if ($widthClampedByWorkArea) { $clampReasons.Add("requested width exceeds the work area") }
    if ($heightClampedByWorkArea) { $clampReasons.Add("requested height exceeds the work area") }
    if ($widthClampedByMinimum) { $clampReasons.Add("window minimum width is larger than requested") }
    if ($widthClampedByResize) { $clampReasons.Add("window manager reduced the requested width") }
    if ($heightClampedByMinimum) { $clampReasons.Add("window minimum height is larger than requested") }
    if ($heightClampedByResize) { $clampReasons.Add("window manager reduced the requested height") }
    $sizeClamped = $clampReasons.Count -gt 0

    $startVisible = Test-ElementOnScreen $startElement $clientScreenRect
    $settingsVisible = Test-ElementOnScreen $settingsElement $clientScreenRect
    $settingsBounds = Get-ElementBounds $settingsElement
    if ($settingsBounds.ControlType -ne "ControlType.Button") {
        throw "Visual smoke failed: expected the Settings button, found $($settingsBounds.ControlType)."
    }

    $settingsInCaptionRegion = $settingsVisible -and (($settingsBounds.X - $clientScreenRect.Left) -ge ($clientScreenRect.Right - $clientScreenRect.Left) * 0.70)
    $metricVisible = Test-ElementOnScreen $metricElement $clientScreenRect
    $highRiskVisible = Test-ElementOnScreen $highRiskElement $clientScreenRect

    $setupScrollState = Get-AutomationScrollState $setupScrollElement
    if ($setupScrollState -eq $null -or !$setupScrollState.VerticallyScrollable) {
        throw "Visual smoke failed: the setup pane is not scrollable, so content below the fold is unreachable."
    }

    $setupStartReachable = $startVisible
    if (!$setupStartReachable) {
        foreach ($scrollPercent in 20, 30, 40, 50, 60, 70, 80, 90) {
            [void](Set-AutomationScrollPercent $setupScrollElement $scrollPercent)
            if (Test-ElementOnScreen $startElement $clientScreenRect) {
                $setupStartReachable = $true
                break
            }
        }
    }

    [void](Set-AutomationScrollPercent $setupScrollElement 100)
    $clearHistoryElement = Find-AutomationElementById $handle "ClearRecentReportsButton" $WaitSeconds
    if (!(Test-ElementOnScreen $clearHistoryElement $clientScreenRect)) {
        [void](Bring-AutomationElementIntoView $clearHistoryElement)
    }

    $setupBottomReachable = Test-ElementOnScreen $clearHistoryElement $clientScreenRect

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $setupBottomScreenshotPath = Join-Path $outputFull "main-setup-bottom-$timestamp.png"
    $setupCapture = Capture-WindowBitmap $handle $rect
    $setupBitmap = $setupCapture.Bitmap
    $setupBitmap.Save($setupBottomScreenshotPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $setupBitmap.Dispose()
    $settingsElement.SetFocus()
    Start-Sleep -Milliseconds 200
    [void](Set-AutomationScrollPercent $setupScrollElement 0)

    $workbenchScrollState = Get-AutomationScrollState $workbenchScrollElement
    if ($workbenchScrollState -eq $null -or !$workbenchScrollState.VerticallyScrollable) {
        throw "Visual smoke failed: the workbench pane is not scrollable."
    }

    [void](Set-AutomationScrollPercent $workbenchScrollElement 100)
    $eventLogElement = Wait-ForAutomationElement $handle "运行记录" $WaitSeconds
    $workbenchTabReachable = Test-ElementOnScreen $eventLogElement $clientScreenRect

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $workbenchBottomScreenshotPath = Join-Path $outputFull "main-workbench-bottom-$timestamp.png"
    $bottomCapture = Capture-WindowBitmap $handle $rect
    $bottomBitmap = $bottomCapture.Bitmap
    $bottomBitmap.Save($workbenchBottomScreenshotPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bottomBitmap.Dispose()

    $settingsElement.SetFocus()
    Start-Sleep -Milliseconds 200
    [void](Set-AutomationScrollPercent $workbenchScrollElement 0)
    [void](Set-AutomationScrollPercent $setupScrollElement 0)
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
        SettingsVisible = $settingsVisible
        SettingsInCaptionRegion = $settingsInCaptionRegion
        SettingsButtonBounds = $settingsBounds
        MetricVisible = $metricVisible
        HighRiskVisible = $highRiskVisible
        SetupScroll = $setupScrollState
        SetupStartReachable = $setupStartReachable
        SetupBottomReachable = $setupBottomReachable
        WorkbenchScroll = $workbenchScrollState
        WorkbenchTabReachable = $workbenchTabReachable
        StartButtonBounds = Get-ElementBounds $startElement
        MetricBounds = Get-ElementBounds $metricElement
        HighRiskBounds = Get-ElementBounds $highRiskElement
        EventLogTabBounds = Get-ElementBounds $eventLogElement
        Samples = $analysis.Samples
        UniqueColors = $analysis.UniqueColors
        Passed = $analysis.IsUseful -and $settingsInCaptionRegion -and $metricVisible -and $highRiskVisible -and $setupStartReachable -and $setupBottomReachable -and $workbenchTabReachable
        CapturedAt = (Get-Date).ToString("o")
    }

    $resultPath = Join-Path $outputFull "visual-smoke-$timestamp.json"
    $result | ConvertTo-Json -Depth 6 | Set-Content -Path $resultPath -Encoding UTF8

    if (!$analysis.IsUseful) {
        throw "Visual smoke failed: screenshot appears blank or undersized. See $screenshotPath"
    }

    if (!$metricVisible -or !$highRiskVisible) {
        throw "Visual smoke failed: the workbench metrics/high-risk panel is not on-screen at the actual size ($([Math]::Round($actualClientWidthDip, 1))x$([Math]::Round($actualClientHeightDip, 1)) DIP). See $screenshotPath"
    }

    if (!$settingsInCaptionRegion) {
        throw "Visual smoke failed: the title-bar Settings button is not aligned with the caption-button region (X=$($settingsBounds.X), client width=$($clientScreenRect.Right - $clientScreenRect.Left))."
    }

    if (!$setupStartReachable -or !$setupBottomReachable) {
        throw "Visual smoke failed: setup content is not reachable by scrolling. See $screenshotPath"
    }

    if (!$workbenchTabReachable) {
        throw "Visual smoke failed: the workbench tabs are not reachable by scrolling. See $workbenchBottomScreenshotPath"
    }

    if ($sizeClamped) {
        Write-Host "Requested $($Width)x$($Height) DIP was clamped to $([Math]::Round($actualClientWidthDip, 1))x$([Math]::Round($actualClientHeightDip, 1)) DIP client area ($($clampReasons -join '; ')). This run verifies only the clamped size."
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
}
