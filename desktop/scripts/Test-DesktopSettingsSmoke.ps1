[CmdletBinding()]
param(
    [string]$PackagePath = "",

    [string]$AppPath = "",

    [string]$OutputDirectory = "",

    [int]$Width = 1600,

    [int]$Height = 1000,

    [int]$WaitSeconds = 8,

    [int]$ThemeIndex = 0,

    [switch]$SkipStartupIntegrationMutation,

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
        Join-Path $scriptDir "smoke-results\settings"
    }
    else {
        Join-Path $repoRoot "artifacts\desktop\settings-smoke"
    }
}

$appFull = [System.IO.Path]::GetFullPath((Resolve-Path $AppPath).Path)
$outputFull = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputFull | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$sandboxDir = Join-Path $outputFull "sandbox-$timestamp"
$inputDir = Join-Path $sandboxDir "input"
New-Item -ItemType Directory -Force -Path $inputDir | Out-Null

$settingsEnvironmentVariable = "EDETECTION_DESKTOP_SETTINGS_DIR"
$previousSettingsOverride = $env:EDETECTION_DESKTOP_SETTINGS_DIR
$settingsDirectory = Join-Path $sandboxDir "settings"
$settingsPath = Join-Path $settingsDirectory "settings.json"
$settingsExisted = Test-Path $settingsPath
$settingsBackup = $null
if ($settingsExisted) {
    $settingsBackup = Get-Content -Path $settingsPath -Raw
}

$runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$runEntryName = "EDetection"
$runEntryExisted = $false
$runEntryBackup = $null
if (Test-Path $runKeyPath) {
    $runEntry = Get-ItemProperty -Path $runKeyPath -Name $runEntryName -ErrorAction SilentlyContinue
    if ($runEntry -ne $null) {
        $runEntryExisted = $true
        $runEntryBackup = $runEntry.PSObject.Properties[$runEntryName].Value
    }
}

$taskName = "EDetection Autostart"

function Get-StartupTaskXml {
    try {
        $output = & schtasks.exe /Query /TN $taskName /XML 2>$null
    }
    catch {
        $global:LASTEXITCODE = 0
        return $null
    }

    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    return ($output -join [Environment]::NewLine)
}

function Remove-StartupTask {
    try {
        & schtasks.exe /Delete /TN $taskName /F 2>$null | Out-Null
    }
    catch {
    }
}

function Restore-StartupTask([string]$TaskXml) {
    if ([string]::IsNullOrWhiteSpace($TaskXml)) {
        return
    }

    $tempPath = Join-Path ([System.IO.Path]::GetTempPath()) "EDetectionDesktopTaskRestore-$([Guid]::NewGuid().ToString('N')).xml"
    try {
        Set-Content -Path $tempPath -Value $TaskXml -Encoding Unicode
        & schtasks.exe /Create /TN $taskName /XML $tempPath /F | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to restore scheduled task '$taskName'."
        }
    }
    finally {
        Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
    }
}

$taskBackup = if ($SkipStartupIntegrationMutation) { $null } else { Get-StartupTaskXml }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class DesktopSettingsSmokeNative
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
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

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
[DesktopSettingsSmokeNative]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class DesktopSettingsMouseNative
{
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
}
"@

$DWMWA_EXTENDED_FRAME_BOUNDS = 9
$PW_RENDERFULLCONTENT = 0x00000002

function Get-VisibleWindowRect([IntPtr]$Handle) {
    $rect = New-Object DesktopSettingsSmokeNative+RECT
    $result = [DesktopSettingsSmokeNative]::DwmGetWindowAttribute(
        $Handle,
        $DWMWA_EXTENDED_FRAME_BOUNDS,
        [ref]$rect,
        [System.Runtime.InteropServices.Marshal]::SizeOf([type][DesktopSettingsSmokeNative+RECT]))
    if ($result -eq 0) {
        return $rect
    }

    if (![DesktopSettingsSmokeNative]::GetWindowRect($Handle, [ref]$rect)) {
        throw "Failed to read window rectangle."
    }

    return $rect
}

function Capture-WindowBitmap([IntPtr]$Handle, [DesktopSettingsSmokeNative+RECT]$Rect) {
    $captureWidth = $Rect.Right - $Rect.Left
    $captureHeight = $Rect.Bottom - $Rect.Top
    if ($captureWidth -le 0 -or $captureHeight -le 0) {
        throw "Invalid window rectangle: $captureWidth x $captureHeight"
    }

    $bufferBitmap = New-Object System.Drawing.Bitmap $captureWidth, $captureHeight
    $bitmap = $null
    $windowDc = [DesktopSettingsSmokeNative]::GetWindowDC($Handle)
    $memoryDc = [IntPtr]::Zero
    $hBitmap = [IntPtr]::Zero
    $oldObject = [IntPtr]::Zero

    try {
        if ($windowDc -eq [IntPtr]::Zero) {
            throw "GetWindowDC returned null."
        }

        $memoryDc = [DesktopSettingsSmokeNative]::CreateCompatibleDC($windowDc)
        if ($memoryDc -eq [IntPtr]::Zero) {
            throw "CreateCompatibleDC returned null."
        }

        $hBitmap = $bufferBitmap.GetHbitmap()
        $oldObject = [DesktopSettingsSmokeNative]::SelectObject($memoryDc, $hBitmap)
        $printed = [DesktopSettingsSmokeNative]::PrintWindow($Handle, $memoryDc, $PW_RENDERFULLCONTENT)
        if (!$printed) {
            $printed = [DesktopSettingsSmokeNative]::PrintWindow($Handle, $memoryDc, 0)
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
            [DesktopSettingsSmokeNative]::SelectObject($memoryDc, $oldObject) | Out-Null
        }

        if ($hBitmap -ne [IntPtr]::Zero) {
            [DesktopSettingsSmokeNative]::DeleteObject($hBitmap) | Out-Null
        }

        if ($memoryDc -ne [IntPtr]::Zero) {
            [DesktopSettingsSmokeNative]::DeleteDC($memoryDc) | Out-Null
        }

        if ($windowDc -ne [IntPtr]::Zero) {
            [DesktopSettingsSmokeNative]::ReleaseDC($Handle, $windowDc) | Out-Null
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

function Test-ElementOnScreen(
    [System.Windows.Automation.AutomationElement]$Element,
    [DesktopSettingsSmokeNative+RECT]$ClientRect) {
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
    return $right - $left -ge [Math]::Min(8, $bounds.Width) -and $bottom - $top -ge [Math]::Min(8, $bounds.Height)
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

function Get-AutomationElementById([IntPtr]$RootHandle, [string]$AutomationId, [int]$TimeoutSeconds) {
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

function Get-AutomationElementByName([IntPtr]$RootHandle, [string]$Name, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($RootHandle)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)

    do {
        $match = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($match -ne $null) {
            return $match
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for automation element '$Name'."
}

function Save-WindowScreenshot(
    [IntPtr]$Handle,
    [string]$OutputDirectory,
    [string]$Label,
    [string]$Timestamp) {
    $rect = Get-VisibleWindowRect $Handle
    $capture = Capture-WindowBitmap $Handle $rect
    $bitmap = $capture.Bitmap
    try {
        $analysis = Test-BitmapIsUseful $bitmap
        if (!$analysis.IsUseful) {
            throw "Settings screenshot '$Label' appears blank or undersized."
        }

        $path = Join-Path $OutputDirectory "$Label-$Timestamp.png"
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        return [pscustomobject]@{
            Path = $path
            Method = $capture.Method
            Width = $bitmap.Width
            Height = $bitmap.Height
            UniqueColors = $analysis.UniqueColors
        }
    }
    finally {
        if ($bitmap -ne $null) {
            $bitmap.Dispose()
        }
    }
}

function Bring-AutomationElementIntoView([IntPtr]$RootHandle, [string]$Name, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($RootHandle)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)

    do {
        $match = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($match -ne $null) {
            try {
                $pattern = $match.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern)
                $pattern.ScrollIntoView()
            }
            catch {
                try {
                    $match.SetFocus()
                }
                catch {
                }
            }

            return $match.Current.Name
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting to bring automation element '$Name' into view."
}

function Wait-ForAutomationNameLike([IntPtr]$RootHandle, [string]$Pattern, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($RootHandle)

    do {
        $matches = $root.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($match in $matches) {
            if ($match.Current.Name -like $Pattern) {
                return $match.Current.Name
            }
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for automation element pattern '$Pattern'."
}

function Expand-AutomationElement([IntPtr]$RootHandle, [string]$Name, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($RootHandle)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)

    do {
        # A toolkit SettingsExpander exposes its header text on the outer
        # SettingsExpander group peer and on the inner Expander button peer, so
        # the first name match is not necessarily the expandable node.  Try
        # every match and report the one that accepted a pattern.
        $matches = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
        foreach ($match in $matches) {
            try {
                $pattern = $match.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
                $pattern.Expand()
                return $match.Current.Name
            }
            catch {
                try {
                    $invoke = $match.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                    $invoke.Invoke()
                    return $match.Current.Name
                }
                catch {
                }
            }
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for expandable automation element '$Name'."
}

function Select-SettingsCategory([IntPtr]$RootHandle, [string]$Name) {
    $identifiers = @{
        "检测" = "SettingsNav_Detection"
        "报告与数据" = "SettingsNav_Reports"
        "常规" = "SettingsNav_General"
        "通知" = "SettingsNav_Notifications"
        "连接与集成" = "SettingsNav_Integrations"
        "更新与维护" = "SettingsNav_Maintenance"
    }

    if (!$identifiers.ContainsKey($Name)) {
        throw "Unknown settings category '$Name'."
    }

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($RootHandle)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $identifiers[$Name])
    $deadline = (Get-Date).AddSeconds($WaitSeconds)

    do {
        $match = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $match) {
            try {
                $pattern = $match.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                if (!$pattern.Current.IsSelected) {
                    $pattern.Select()
                }
                Start-Sleep -Milliseconds 200
                if ($pattern.Current.IsSelected) {
                    return
                }
            }
            catch [System.Windows.Automation.ElementNotAvailableException] {
                # A WinUI navigation item may be re-created while its layout updates.
            }
            catch [System.InvalidOperationException] {
                throw "Settings category '$Name' has no valid UIA selection pattern."
            }
        }

        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)

    throw "Settings category '$Name' was not selected (AutomationId=$($identifiers[$Name]))."
}

function Test-SettingsPageVisible([IntPtr]$RootHandle) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($RootHandle)
    $back = $root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            "SettingsBackButton")))
    return $back -ne $null -and !$back.Current.IsOffscreen
}

function Test-EscapePopupVisible([IntPtr]$RootHandle) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($RootHandle)
    $match = $root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            "Esc")))
    return $match -ne $null -and !$match.Current.IsOffscreen
}

function Send-EscapeToWindow([IntPtr]$Handle) {
    [DesktopSettingsSmokeNative]::SetForegroundWindow($Handle) | Out-Null
    Start-Sleep -Milliseconds 250
    if ([DesktopSettingsSmokeNative]::GetForegroundWindow() -eq $Handle) {
        [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
    }
    else {
        [DesktopSettingsSmokeNative]::PostMessage($Handle, 0x0100, [IntPtr]0x1B, [IntPtr]0) | Out-Null
    }

    Start-Sleep -Milliseconds 900
}

function Open-SettingsFromGear([IntPtr]$RootHandle) {
    $gear = Get-AutomationElementById $RootHandle "SettingsTitleBarButton" $WaitSeconds
    $gear.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    [void](Wait-ForAutomationName $RootHandle "搜索设置" $WaitSeconds)
    Start-Sleep -Milliseconds 300
}

function Get-AutomationElementByNameAndControlType(
    [IntPtr]$RootHandle,
    [string]$Name,
    [string]$ControlTypeProgrammaticName,
    [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($RootHandle)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)

    do {
        $matches = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
        foreach ($match in $matches) {
            if ($match.Current.ControlType.ProgrammaticName -eq $ControlTypeProgrammaticName) {
                return $match
            }
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for automation element '$Name' of type '$ControlTypeProgrammaticName'."
}

function Set-SettingsSearchFocus([IntPtr]$RootHandle) {
    $search = Get-AutomationElementByName $RootHandle "搜索设置" $WaitSeconds
    try {
        $search.SetFocus()
        return $true
    }
    catch {
        # AutoSuggestBox exposes its text editor as a child element.
        $editCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Edit)
        $edit = $search.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCondition)
        if ($edit -ne $null) {
            try {
                $edit.SetFocus()
                return $true
            }
            catch {
            }
        }
    }

    return $false
}

function Set-SettingsSearchText([IntPtr]$RootHandle, [string]$Text) {
    $search = Get-AutomationElementByName $RootHandle "搜索设置" $WaitSeconds
    $editCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)
    $target = $search.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCondition)
    if ($target -eq $null) {
        $target = $search
    }

    try {
        $valuePattern = [System.Windows.Automation.ValuePattern]$target.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern)
        $valuePattern.SetValue($Text)
        return $true
    }
    catch {
        return $false
    }
}

$process = $null

try {
    $env:EDETECTION_DESKTOP_SETTINGS_DIR = $settingsDirectory
    New-Item -ItemType Directory -Force -Path $settingsDirectory | Out-Null

    if (!$SkipStartupIntegrationMutation) {
        if (!(Test-Path $runKeyPath)) {
            New-Item -Path $runKeyPath -Force | Out-Null
        }

        Remove-ItemProperty -Path $runKeyPath -Name $runEntryName -ErrorAction SilentlyContinue
        Remove-StartupTask
    }

    $seedSettings = [ordered]@{
        InputDirectory = $inputDir
        OutputDirectory = ""
        ConfigPath = "config.json"
        WriteReport = $false
        CloseToTrayOnClose = $true
        StartMinimizedToTray = $false
        AutoStartOnSignIn = $false
        EnableDesktopNotifications = $false
        EnableLlmAssistant = $true
        LlmEndpoint = "https://api.example.test/v1/chat/completions"
        LlmModel = "example-model"
        UseProxyForLlm = $true
        EnableNtfyNotifications = $true
        NtfyServerUrl = "https://ntfy.sh"
        NtfyTopic = "edetection-smoke"
        SelectedNtfyPriorityIndex = 2
        UseProxyForNotifications = $true
        EnableNetworkProxy = $true
        ProxyAddress = "http://127.0.0.1:7890"
        ProxyRequiresAuthentication = $false
        ProxyUserName = ""
        EnableUpdateChecks = $true
        UseProxyForUpdates = $true
        SelectedUpdateChannelIndex = 1
        UpdateFeedUrl = "https://github.com/osGex0o0II/E-Detection/releases/latest"
        SelectedLogRetentionIndex = 2
        SelectedRecentReportLimitIndex = 2
        SelectedThemeIndex = $ThemeIndex
        SelectedBackdropIndex = 0
        EnablePoetryStatus = $true
        PoetryServiceUrl = "https://poetry.palemoky.com/"
        SelectedPoetryLanguageIndex = 0
        RecentReports = @()
    }
    [pscustomobject]$seedSettings | ConvertTo-Json | Set-Content -Path $settingsPath -Encoding UTF8

    $process = Start-Process -FilePath $appFull -WorkingDirectory (Split-Path -Parent $appFull) -PassThru
    $mainWindow = Wait-ForWindowTitle $process.Id "*E-Detection*" $WaitSeconds
    [DesktopSettingsSmokeNative]::ShowWindow($mainWindow.Handle, 9) | Out-Null

    $dpi = [DesktopSettingsSmokeNative]::GetDpiForWindow($mainWindow.Handle)
    if ($dpi -eq 0) {
        $dpi = 96
    }

    $scale = $dpi / 96.0
    $workArea = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea

    # Measure the non-client frame before resizing so the requested size is a
    # client-size request. MoveWindow sizes the outer window.
    $initialWindowRect = New-Object DesktopSettingsSmokeNative+RECT
    if (![DesktopSettingsSmokeNative]::GetWindowRect($mainWindow.Handle, [ref]$initialWindowRect)) {
        throw "Settings smoke: failed to read the initial window rectangle."
    }

    $initialClientRect = New-Object DesktopSettingsSmokeNative+RECT
    if (![DesktopSettingsSmokeNative]::GetClientRect($mainWindow.Handle, [ref]$initialClientRect)) {
        throw "Settings smoke: failed to read the initial client rectangle."
    }

    $frameWidthPhysical = ($initialWindowRect.Right - $initialWindowRect.Left) - ($initialClientRect.Right - $initialClientRect.Left)
    $frameHeightPhysical = ($initialWindowRect.Bottom - $initialWindowRect.Top) - ($initialClientRect.Bottom - $initialClientRect.Top)
    $requestedClientWidthPhysical = [Math]::Ceiling($Width * $scale)
    $requestedClientHeightPhysical = [Math]::Ceiling($Height * $scale)
    $desiredOuterWidth = $requestedClientWidthPhysical + [Math]::Max(0, $frameWidthPhysical)
    $desiredOuterHeight = $requestedClientHeightPhysical + [Math]::Max(0, $frameHeightPhysical)
    $targetWidth = [Math]::Min($desiredOuterWidth, $workArea.Width)
    $targetHeight = [Math]::Min($desiredOuterHeight, $workArea.Height)
    [DesktopSettingsSmokeNative]::MoveWindow($mainWindow.Handle, $workArea.Left, $workArea.Top, $targetWidth, $targetHeight, $true) | Out-Null
    [DesktopSettingsSmokeNative]::SetForegroundWindow($mainWindow.Handle) | Out-Null
    Start-Sleep -Milliseconds 700

    $windowRect = Get-VisibleWindowRect $mainWindow.Handle
    $clientRect = New-Object DesktopSettingsSmokeNative+RECT
    if (![DesktopSettingsSmokeNative]::GetClientRect($mainWindow.Handle, [ref]$clientRect)) {
        throw "Settings smoke: failed to read the client rectangle."
    }

    $clientOrigin = New-Object DesktopSettingsSmokeNative+POINT
    if (![DesktopSettingsSmokeNative]::ClientToScreen($mainWindow.Handle, [ref]$clientOrigin)) {
        throw "Settings smoke: failed to map the client rectangle to screen coordinates."
    }

    $clientScreenRect = New-Object DesktopSettingsSmokeNative+RECT
    $clientScreenRect.Left = $clientOrigin.X
    $clientScreenRect.Top = $clientOrigin.Y
    $clientScreenRect.Right = $clientOrigin.X + $clientRect.Right
    $clientScreenRect.Bottom = $clientOrigin.Y + $clientRect.Bottom

    $actualOuterWidthDip = ($windowRect.Right - $windowRect.Left) / $scale
    $actualOuterHeightDip = ($windowRect.Bottom - $windowRect.Top) / $scale
    $actualClientWidthDip = ($clientRect.Right - $clientRect.Left) / $scale
    $actualClientHeightDip = ($clientRect.Bottom - $clientRect.Top) / $scale
    $sizeToleranceDip = 2
    $clampReasons = [System.Collections.Generic.List[string]]::new()
    if ($desiredOuterWidth -gt $workArea.Width) { $clampReasons.Add("requested width exceeds the work area") }
    if ($desiredOuterHeight -gt $workArea.Height) { $clampReasons.Add("requested height exceeds the work area") }
    if ($actualClientWidthDip -gt $Width + $sizeToleranceDip) { $clampReasons.Add("window minimum width is larger than requested") }
    if ($actualClientWidthDip -lt $Width - $sizeToleranceDip) { $clampReasons.Add("window manager reduced the requested width") }
    if ($actualClientHeightDip -gt $Height + $sizeToleranceDip) { $clampReasons.Add("window minimum height is larger than requested") }
    if ($actualClientHeightDip -lt $Height - $sizeToleranceDip) { $clampReasons.Add("window manager reduced the requested height") }
    $sizeClamped = $clampReasons.Count -gt 0

    $screenshotTimestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $settingsScreenshots = [ordered]@{}
    $mainScreenshot = Save-WindowScreenshot $mainWindow.Handle $outputFull "main-before-settings" $screenshotTimestamp
    $settingsScreenshots["Main"] = $mainScreenshot.Path

    $settingsButtonElement = Get-AutomationElementById $mainWindow.Handle "SettingsTitleBarButton" 2
    if ($settingsButtonElement -eq $null) {
        throw "Settings control cannot be found through UI Automation."
    }

    $reportHistorySearchElement = Get-AutomationElementById $mainWindow.Handle "ReportHistorySearchBox" $WaitSeconds
    $reportHistoryFilterElement = Get-AutomationElementById $mainWindow.Handle "ReportHistoryFilterComboBox" $WaitSeconds
    $detailTableElement = Get-AutomationElementById $mainWindow.Handle "DetailTable" $WaitSeconds
    $workbenchAccessibleNames = [ordered]@{
        "ReportHistorySearch" = $reportHistorySearchElement.Current.Name
        "ReportHistoryFilter" = $reportHistoryFilterElement.Current.Name
        "DetailTable" = $detailTableElement.Current.Name
    }
    if ($workbenchAccessibleNames["ReportHistorySearch"] -ne "搜索报告历史") {
        throw "Settings smoke: report history search accessible name is '$($workbenchAccessibleNames["ReportHistorySearch"])'."
    }

    if ($workbenchAccessibleNames["ReportHistoryFilter"] -ne "筛选报告历史") {
        throw "Settings smoke: report history filter accessible name is '$($workbenchAccessibleNames["ReportHistoryFilter"])'."
    }

    if ($workbenchAccessibleNames["DetailTable"] -ne "异常明细表") {
        throw "Settings smoke: detail table accessible name is '$($workbenchAccessibleNames["DetailTable"])'."
    }

    $settingsButtonElement.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $settingsTitle = Wait-ForAutomationName $mainWindow.Handle "设置" $WaitSeconds
    $settingsSearchBox = Wait-ForAutomationName $mainWindow.Handle "搜索设置" $WaitSeconds

    $settingsNavExpected = [ordered]@{
        "SettingsNav_Detection" = "检测"
        "SettingsNav_Reports" = "报告与数据"
        "SettingsNav_General" = "常规"
        "SettingsNav_Notifications" = "通知"
        "SettingsNav_Integrations" = "连接与集成"
        "SettingsNav_Maintenance" = "更新与维护"
    }
    $settingsNavigationNames = [ordered]@{}
    foreach ($entry in $settingsNavExpected.GetEnumerator()) {
        $navElement = Get-AutomationElementById $mainWindow.Handle $entry.Key $WaitSeconds
        $settingsNavigationNames[$entry.Key] = $navElement.Current.Name
        if ($navElement.Current.Name -ne $entry.Value) {
            throw "Settings smoke: navigation item $($entry.Key) is named '$($navElement.Current.Name)', expected '$($entry.Value)'."
        }
    }

    $backElement = Get-AutomationElementByName $mainWindow.Handle "返回工作台" $WaitSeconds
    $backBounds = Get-ElementBounds $backElement
    $windowCenterX = ($windowRect.Left + $windowRect.Right) / 2
    $backIsTopLeft = $backBounds.X -lt $windowCenterX
    if (!$backIsTopLeft) {
        throw "Settings smoke: the back button is not in the top-left region (X=$($backBounds.X), center=$windowCenterX)."
    }

    $settingsSearchVisible = Test-ElementOnScreen (Get-AutomationElementByName $mainWindow.Handle "搜索设置" $WaitSeconds) $clientScreenRect

    # Settings search must explain a zero-result query instead of silently
    # leaving an empty suggestion popup.
    [DesktopSettingsSmokeNative]::SetForegroundWindow($mainWindow.Handle) | Out-Null
    Start-Sleep -Milliseconds 250
    $settingsSearchFocusedForNoResults = Set-SettingsSearchFocus $mainWindow.Handle
    if (!$settingsSearchFocusedForNoResults) {
        throw "Settings smoke: could not focus the Settings search box for the no-results check."
    }
    # Typing through SendKeys is blocked by some endpoint-protection setups,
    # so drive the same code path through the automation value pattern.
    if (!(Set-SettingsSearchText $mainWindow.Handle "zzzz")) {
        throw "Settings smoke: could not type into the Settings search box for the no-results check."
    }
    Start-Sleep -Milliseconds 700
    $settingsSearchNoResultsElement = Get-AutomationElementById $mainWindow.Handle "SettingsSearchNoResults" 3
    $settingsSearchNoResultsVisible = Test-ElementOnScreen $settingsSearchNoResultsElement $clientScreenRect
    if (!$settingsSearchNoResultsVisible) {
        throw "Settings smoke: a zero-result search did not show the no-results state."
    }
    $settingsScreenshots["SearchNoResults"] = (Save-WindowScreenshot $mainWindow.Handle $outputFull "settings-search-no-results" $screenshotTimestamp).Path
    Set-SettingsSearchText $mainWindow.Handle "" | Out-Null
    Start-Sleep -Milliseconds 700
    $settingsSearchNoResultsCleared = $false
    try {
        $settingsSearchNoResultsAfterClear = Get-AutomationElementById $mainWindow.Handle "SettingsSearchNoResults" 1
        $settingsSearchNoResultsCleared = $settingsSearchNoResultsAfterClear.Current.IsOffscreen
    }
    catch {
        $settingsSearchNoResultsCleared = $true
    }
    if (!$settingsSearchNoResultsCleared) {
        throw "Settings smoke: clearing the Settings search did not hide the no-results state."
    }

    # Choosing a search result must move keyboard focus to the target section,
    # not leave it stranded in the navigation rail.  Typing via SendKeys is
    # blocked by some endpoint-protection setups, so set the text through the
    # value pattern and only send the Enter keystroke.  The keystroke can also
    # be dropped when another process steals the foreground, so retry.
    $settingsSearchNavigationFocus = ""
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        [DesktopSettingsSmokeNative]::SetForegroundWindow($mainWindow.Handle) | Out-Null
        Start-Sleep -Milliseconds 200
        Set-SettingsSearchFocus $mainWindow.Handle | Out-Null
        Set-SettingsSearchText $mainWindow.Handle "mica" | Out-Null
        Start-Sleep -Milliseconds 400
        [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
        Start-Sleep -Milliseconds 1200
        $settingsSearchFocusedElement = [System.Windows.Automation.AutomationElement]::FocusedElement
        $settingsSearchNavigationFocus = if ($settingsSearchFocusedElement -eq $null) {
            ""
        }
        else {
            $settingsSearchFocusedElement.Current.Name
        }
        if ($settingsSearchNavigationFocus -eq "应用主题") {
            break
        }
    }
    if ($settingsSearchNavigationFocus -ne "应用主题") {
        $focusCodes = ($settingsSearchNavigationFocus.ToCharArray() | ForEach-Object { [int]$_ }) -join ','
        throw "Settings smoke: searching for a setting did not move focus to its target (focused '$settingsSearchNavigationFocus', codes=$focusCodes)."
    }

    Select-SettingsCategory $mainWindow.Handle "检测"
    Start-Sleep -Milliseconds 500
    $settingsScreenshots["Detection"] = (Save-WindowScreenshot $mainWindow.Handle $outputFull "settings-detection" $screenshotTimestamp).Path
    $saveSettingsElement = Get-AutomationElementByName $mainWindow.Handle "保存检测阈值" $WaitSeconds
    $resetSettingsElement = Get-AutomationElementByName $mainWindow.Handle "重置设置" $WaitSeconds
    $settingsSaveVisible = Test-ElementOnScreen $saveSettingsElement $clientScreenRect
    $settingsResetVisible = Test-ElementOnScreen $resetSettingsElement $clientScreenRect
    if (!$settingsSaveVisible -or !$settingsResetVisible -or !$settingsSearchVisible) {
        throw "Settings smoke: key settings controls are not on-screen at $([Math]::Round($actualClientWidthDip, 1))x$([Math]::Round($actualClientHeightDip, 1)) DIP."
    }

    $defaultsSection = Wait-ForAutomationName $mainWindow.Handle "检测" $WaitSeconds
    $openThresholdSettingsButton = Wait-ForAutomationName $mainWindow.Handle "打开阈值设置" $WaitSeconds
    $openDetectionRulesButton = Wait-ForAutomationName $mainWindow.Handle "打开检测规则" $WaitSeconds
    $thresholdsSection = Wait-ForAutomationName $mainWindow.Handle "检测阈值" $WaitSeconds
    $thresholdConfigControl = Wait-ForAutomationName $mainWindow.Handle "阈值文件路径" $WaitSeconds
    $browseThresholdConfigButton = Wait-ForAutomationName $mainWindow.Handle "选择阈值文件" $WaitSeconds
    $loadThresholdConfigButton = Wait-ForAutomationName $mainWindow.Handle "加载阈值文件" $WaitSeconds
    $saveAsThresholdConfigButton = Wait-ForAutomationName $mainWindow.Handle "另存为阈值文件" $WaitSeconds
    $rulesSection = Wait-ForAutomationName $mainWindow.Handle "检测规则" $WaitSeconds
    $voltageMinThreshold = Wait-ForAutomationName $mainWindow.Handle "电压下限" $WaitSeconds
    $voltageMaxThreshold = Wait-ForAutomationName $mainWindow.Handle "电压上限" $WaitSeconds
    $currentMaxThreshold = Wait-ForAutomationName $mainWindow.Handle "电流上限" $WaitSeconds
    $powerFactorMinThreshold = Wait-ForAutomationName $mainWindow.Handle "功率因数下限" $WaitSeconds
    $temperatureMaxThreshold = Wait-ForAutomationName $mainWindow.Handle "温度上限" $WaitSeconds
    $freezeCountThreshold = Wait-ForAutomationName $mainWindow.Handle "连续不变化点数" $WaitSeconds
    $moreThresholdsExpander = Expand-AutomationElement $mainWindow.Handle "更多阈值" $WaitSeconds
    Start-Sleep -Milliseconds 700
    Bring-AutomationElementIntoView $mainWindow.Handle "更多阈值" $WaitSeconds | Out-Null
    $currentUnbalanceMaxThreshold = Wait-ForAutomationName $mainWindow.Handle "电流不平衡上限" $WaitSeconds
    $activePowerMinThreshold = Wait-ForAutomationName $mainWindow.Handle "有功功率下限" $WaitSeconds
    $temperatureMinThreshold = Wait-ForAutomationName $mainWindow.Handle "温度下限" $WaitSeconds
    $currentActiveMinThreshold = Wait-ForAutomationName $mainWindow.Handle "电流激活下限" $WaitSeconds
    $freezeStdThreshold = Wait-ForAutomationName $mainWindow.Handle "数据波动容差" $WaitSeconds
    $voltageImbalanceThreshold = Wait-ForAutomationName $mainWindow.Handle "相电压不平衡阈值" $WaitSeconds
    $voltageUnitHint = Wait-ForAutomationName $mainWindow.Handle "单位 V · 0-1000" $WaitSeconds
    $currentUnitHint = Wait-ForAutomationName $mainWindow.Handle "单位 A" $WaitSeconds
    $ratioUnitHint = Wait-ForAutomationNameLike $mainWindow.Handle "比例 · 0-1*" $WaitSeconds
    $sampleCountHint = Wait-ForAutomationName $mainWindow.Handle "采样点数 · 1-1000" $WaitSeconds
    $currentOverloadRule = Wait-ForAutomationName $mainWindow.Handle "电流过载检测" $WaitSeconds
    $currentUnbalanceRule = Wait-ForAutomationName $mainWindow.Handle "电流不平衡检测" $WaitSeconds
    $powerFactorRule = Wait-ForAutomationName $mainWindow.Handle "功率因数检测" $WaitSeconds
    $detailOutputRule = Wait-ForAutomationName $mainWindow.Handle "详细异常输出" $WaitSeconds
    $saveSettingsButton = Wait-ForAutomationName $mainWindow.Handle "保存检测阈值" $WaitSeconds

    Select-SettingsCategory $mainWindow.Handle "报告与数据"
    $reportsSection = Wait-ForAutomationName $mainWindow.Handle "报告" $WaitSeconds
    $logsSection = Wait-ForAutomationName $mainWindow.Handle "运行记录" $WaitSeconds
    $recentLimitControl = Wait-ForAutomationName $mainWindow.Handle "报告历史上限" $WaitSeconds
    $logLimitControl = Wait-ForAutomationName $mainWindow.Handle "运行记录上限" $WaitSeconds
    $settingsScreenshots["Reports"] = (Save-WindowScreenshot $mainWindow.Handle $outputFull "settings-reports" $screenshotTimestamp).Path

    Select-SettingsCategory $mainWindow.Handle "常规"
    $themeControl = Wait-ForAutomationName $mainWindow.Handle "应用主题" $WaitSeconds
    $backdropControl = Wait-ForAutomationName $mainWindow.Handle "窗口背景" $WaitSeconds
    $poetryToggle = Wait-ForAutomationName $mainWindow.Handle "顶部诗词" $WaitSeconds
    $poetryOptionsExpander = Expand-AutomationElement $mainWindow.Handle "诗词显示选项（可选）" $WaitSeconds
    Start-Sleep -Milliseconds 300
    $poetryServiceControl = Wait-ForAutomationName $mainWindow.Handle "顶部诗词来源" $WaitSeconds
    $poetryLanguageControl = Wait-ForAutomationName $mainWindow.Handle "诗词语言" $WaitSeconds
    $settingsScreenshots["General"] = (Save-WindowScreenshot $mainWindow.Handle $outputFull "settings-general" $screenshotTimestamp).Path

    $startupTrayToggle = Wait-ForAutomationName $mainWindow.Handle "启动时隐藏到托盘" $WaitSeconds
    $autoStartToggle = Wait-ForAutomationName $mainWindow.Handle "登录后自动启动" $WaitSeconds
    $openWindowsStartupSettingsButton = Wait-ForAutomationName $mainWindow.Handle "打开 Windows 启动应用设置" $WaitSeconds
    $resetSettingsButton = Wait-ForAutomationName $mainWindow.Handle "重置设置" $WaitSeconds

    Select-SettingsCategory $mainWindow.Handle "通知"
    $desktopNotificationToggle = Wait-ForAutomationName $mainWindow.Handle "桌面通知" $WaitSeconds
    $desktopNotificationTestButton = Wait-ForAutomationName $mainWindow.Handle "测试桌面通知" $WaitSeconds
    $desktopNotificationTestElement = Get-AutomationElementByName $mainWindow.Handle "测试桌面通知" $WaitSeconds
    $openWindowsNotificationSettingsButton = Wait-ForAutomationName $mainWindow.Handle "打开 Windows 通知设置" $WaitSeconds
    $ntfySection = Wait-ForAutomationName $mainWindow.Handle "外部消息推送" $WaitSeconds
    $ntfySettingsExpander = Expand-AutomationElement $mainWindow.Handle "推送服务配置（启用后生效）" $WaitSeconds
    Start-Sleep -Milliseconds 300
    $ntfyServerControl = Wait-ForAutomationName $mainWindow.Handle "ntfy 服务地址" $WaitSeconds
    $ntfyProxyToggle = Wait-ForAutomationName $mainWindow.Handle "ntfy 使用网络代理" $WaitSeconds
    $ntfyTestButton = Wait-ForAutomationName $mainWindow.Handle "发送 ntfy 测试推送" $WaitSeconds
    $settingsScreenshots["Notifications"] = (Save-WindowScreenshot $mainWindow.Handle $outputFull "settings-notifications" $screenshotTimestamp).Path

    # Category navigation must dismiss feedback raised by an action in the
    # previous category; otherwise the new page looks like it failed.
    $desktopNotificationTestElement.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 800
    $settingsFeedbackElement = Get-AutomationElementById $mainWindow.Handle "SettingsFeedbackBar" 3
    $settingsFeedbackVisible = Test-ElementOnScreen $settingsFeedbackElement $clientScreenRect
    if (!$settingsFeedbackVisible) {
        throw "Settings smoke: test notification did not show the footer feedback bar."
    }
    $settingsScreenshots["Feedback"] = (Save-WindowScreenshot $mainWindow.Handle $outputFull "settings-footer-feedback" $screenshotTimestamp).Path
    Select-SettingsCategory $mainWindow.Handle "常规"
    Start-Sleep -Milliseconds 600
    $settingsFeedbackClearedOnNavigation = $false
    try {
        $settingsFeedbackAfterNavigation = Get-AutomationElementById $mainWindow.Handle "SettingsFeedbackBar" 1
        $settingsFeedbackClearedOnNavigation = $settingsFeedbackAfterNavigation.Current.IsOffscreen
    }
    catch {
        $settingsFeedbackClearedOnNavigation = $true
    }
    if (!$settingsFeedbackClearedOnNavigation) {
        throw "Settings smoke: footer feedback stayed open after switching settings categories."
    }

    Select-SettingsCategory $mainWindow.Handle "连接与集成"
    $llmSection = Wait-ForAutomationName $mainWindow.Handle "智能助手集成" $WaitSeconds
    $llmConnectionExpander = Expand-AutomationElement $mainWindow.Handle "连接与凭据（启用后生效）" $WaitSeconds
    Start-Sleep -Milliseconds 300
    $llmEndpointControl = Wait-ForAutomationName $mainWindow.Handle "LLM 服务地址" $WaitSeconds
    $llmProxyToggle = Wait-ForAutomationName $mainWindow.Handle "LLM 使用网络代理" $WaitSeconds
    $llmTestButton = Wait-ForAutomationName $mainWindow.Handle "测试 LLM 连接" $WaitSeconds
    $proxySection = Wait-ForAutomationName $mainWindow.Handle "网络连接" $WaitSeconds
    $proxyAddressControl = Wait-ForAutomationName $mainWindow.Handle "代理地址" $WaitSeconds
    $proxyTestButton = Wait-ForAutomationName $mainWindow.Handle "测试网络代理" $WaitSeconds
    $openWindowsProxySettingsButton = Wait-ForAutomationName $mainWindow.Handle "打开 Windows 代理设置" $WaitSeconds
    $settingsScreenshots["Integrations"] = (Save-WindowScreenshot $mainWindow.Handle $outputFull "settings-integrations" $screenshotTimestamp).Path

    Select-SettingsCategory $mainWindow.Handle "更新与维护"
    $diagnosticsSection = Wait-ForAutomationName $mainWindow.Handle "维护" $WaitSeconds
    $diagnosticsExpander = Expand-AutomationElement $mainWindow.Handle "维护详情" $WaitSeconds
    Start-Sleep -Milliseconds 300
    $updatesSection = Wait-ForAutomationName $mainWindow.Handle "应用更新" $WaitSeconds
    $updateStatusText = Wait-ForAutomationNameLike $mainWindow.Handle "*当前版本*" $WaitSeconds
    $updateStatusElement = Get-AutomationElementByName $mainWindow.Handle $updateStatusText $WaitSeconds
    $updateStatusVisibleText = $updateStatusText
    try {
        $textPattern = [System.Windows.Automation.TextPattern]$updateStatusElement.GetCurrentPattern(
            [System.Windows.Automation.TextPattern]::Pattern)
        $visibleText = $textPattern.DocumentRange.GetText(-1)
        if (![string]::IsNullOrWhiteSpace($visibleText)) {
            $updateStatusVisibleText = $visibleText.Trim()
        }
    }
    catch {
        # TextBlock may not expose TextPattern in every WinUI build; the
        # automation name is still checked below.
    }

    $fullVersionHash = $null
    $shortVersionHash = $null
    if ($updateStatusText -match '^当前版本\s+\S+\+([0-9a-fA-F]+)$') {
        $fullVersionHash = $Matches[1]
        if ($fullVersionHash.Length -lt 7) {
            throw "Settings smoke: automation version '$updateStatusText' does not contain a usable commit hash."
        }

        if ($updateStatusVisibleText -match '\+([0-9a-fA-F]+)$') {
            $shortVersionHash = $Matches[1]
            if ($shortVersionHash.Length -ne 7) {
                throw "Settings smoke: visible version '$updateStatusVisibleText' does not use a 7-character short hash."
            }

            if (!$fullVersionHash.StartsWith($shortVersionHash, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Settings smoke: visible short hash '$shortVersionHash' is not a prefix of the full hash '$fullVersionHash'."
            }
        }
    }

    $releaseInfo = Join-Path (Split-Path -Parent $appFull) "release-info.txt"
    if (Test-Path -LiteralPath $releaseInfo) {
        $versionLine = Get-Content -LiteralPath $releaseInfo | Where-Object { $_ -like "Version=*" } | Select-Object -First 1
        if (!$versionLine) {
            throw "Settings smoke: release-info.txt has no Version value."
        }

        $expectedVersion = ($versionLine -replace '^Version=', '').Trim()
        if ($updateStatusText -notmatch ("^当前版本\s+" + [regex]::Escape($expectedVersion) + "(?:\+.*)?$")) {
            throw "Settings smoke: displayed version '$updateStatusText' differs from package version '$expectedVersion'."
        }
    }

    $checkUpdatesButton = Wait-ForAutomationName $mainWindow.Handle "检查更新" $WaitSeconds
    $openUpdatePageButton = Wait-ForAutomationName $mainWindow.Handle "获取更新" $WaitSeconds
    $advancedUpdateExpander = Expand-AutomationElement $mainWindow.Handle "高级更新选项" $WaitSeconds
    Start-Sleep -Milliseconds 300
    $updateProxyToggle = Wait-ForAutomationName $mainWindow.Handle "更新使用网络代理" $WaitSeconds
    $updateFeedControl = Wait-ForAutomationName $mainWindow.Handle "更新源" $WaitSeconds
    $desktopHealthSection = Wait-ForAutomationName $mainWindow.Handle "应用状态" $WaitSeconds
    $settingsScreenshots["Maintenance"] = (Save-WindowScreenshot $mainWindow.Handle $outputFull "settings-maintenance" $screenshotTimestamp).Path

    # Escape must close Settings from any focused control and return focus to
    # the title-bar gear, so a keyboard user is not stranded on a collapsed page.
    $escapeSearch = Get-AutomationElementByName $mainWindow.Handle "搜索设置" $WaitSeconds
    $escapeSearchFocused = Set-SettingsSearchFocus $mainWindow.Handle
    if (!$escapeSearchFocused) {
        throw "Settings smoke: could not focus the Settings search box through UI Automation."
    }
    Start-Sleep -Milliseconds 400
    $stuckEscBeforeSearchEscape = Test-EscapePopupVisible $mainWindow.Handle
    Send-EscapeToWindow $mainWindow.Handle
    $settingsClosedBySearchEscape = -not (Test-SettingsPageVisible $mainWindow.Handle)
    $focusedAfterSearchEscape = [System.Windows.Automation.AutomationElement]::FocusedElement
    $focusRestoredAfterSearchEscape = ($focusedAfterSearchEscape -ne $null) -and ($focusedAfterSearchEscape.Current.AutomationId -eq "SettingsTitleBarButton")
    $stuckEscAfterSearchEscape = Test-EscapePopupVisible $mainWindow.Handle
    if (!$settingsClosedBySearchEscape) {
        throw "Settings smoke: Escape did not close the Settings page from the search box."
    }

    if (!$focusRestoredAfterSearchEscape) {
        throw "Settings smoke: Escape from the search box did not restore focus to the title-bar Settings button."
    }

    if ($stuckEscBeforeSearchEscape -or $stuckEscAfterSearchEscape) {
        throw "Settings smoke: a stray 'Esc' accelerator popup is visible over the page."
    }

    # Ctrl+, is the conventional Settings shortcut and must work from the shell.
    [DesktopSettingsSmokeNative]::SetForegroundWindow($mainWindow.Handle) | Out-Null
    Start-Sleep -Milliseconds 300
    $ctrlCommaTested = [DesktopSettingsSmokeNative]::GetForegroundWindow() -eq $mainWindow.Handle
    $ctrlCommaOpenedSettings = $false
    if ($ctrlCommaTested) {
        [System.Windows.Forms.SendKeys]::SendWait("^,")
        Start-Sleep -Milliseconds 900
        $ctrlCommaOpenedSettings = Test-SettingsPageVisible $mainWindow.Handle
        if (!$ctrlCommaOpenedSettings) {
            throw "Settings smoke: Ctrl+, did not open the Settings page."
        }
    }
    else {
        Open-SettingsFromGear $mainWindow.Handle
    }

    # Alt+Left returns from Settings to the workbench.
    $altLeftTested = [DesktopSettingsSmokeNative]::GetForegroundWindow() -eq $mainWindow.Handle
    $altLeftClosedSettings = $false
    if ($altLeftTested) {
        [System.Windows.Forms.SendKeys]::SendWait("%{LEFT}")
        Start-Sleep -Milliseconds 900
        $altLeftClosedSettings = -not (Test-SettingsPageVisible $mainWindow.Handle)
        if (!$altLeftClosedSettings) {
            throw "Settings smoke: Alt+Left did not return to the workbench."
        }
    }
    else {
        $escapeNavButton = Get-AutomationElementByName $mainWindow.Handle "返回工作台" $WaitSeconds
        $escapeNavButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 700
        $altLeftClosedSettings = $false
    }

    if (!(Test-SettingsPageVisible $mainWindow.Handle)) {
        Open-SettingsFromGear $mainWindow.Handle
    }

    # Escape must also work after focus moved through navigation.
    $escapeNav = Get-AutomationElementById $mainWindow.Handle "SettingsNav_Detection" $WaitSeconds
    $escapeNav.SetFocus()
    Start-Sleep -Milliseconds 400
    Send-EscapeToWindow $mainWindow.Handle
    $settingsClosedByEscape = -not (Test-SettingsPageVisible $mainWindow.Handle)
    $focusedAfterEscape = [System.Windows.Automation.AutomationElement]::FocusedElement
    $focusRestoredAfterEscape = ($focusedAfterEscape -ne $null) -and ($focusedAfterEscape.Current.AutomationId -eq "SettingsTitleBarButton")
    if (!$settingsClosedByEscape) {
        throw "Settings smoke: Escape did not close the Settings page from navigation."
    }

    if (!$focusRestoredAfterEscape) {
        throw "Settings smoke: Escape from navigation did not restore focus to the title-bar Settings button."
    }

    # A focused ComboBox and a focused TextBox must also hand Escape to the page.
    Open-SettingsFromGear $mainWindow.Handle
    Select-SettingsCategory $mainWindow.Handle "报告与数据"
    $settingsComboBox = Get-AutomationElementByNameAndControlType $mainWindow.Handle "报告历史上限" "ControlType.ComboBox" $WaitSeconds
    $settingsComboBox.SetFocus()
    Start-Sleep -Milliseconds 300
    Send-EscapeToWindow $mainWindow.Handle
    $settingsClosedByComboEscape = -not (Test-SettingsPageVisible $mainWindow.Handle)
    $focusedAfterComboEscape = [System.Windows.Automation.AutomationElement]::FocusedElement
    $focusRestoredAfterComboEscape = ($focusedAfterComboEscape -ne $null) -and ($focusedAfterComboEscape.Current.AutomationId -eq "SettingsTitleBarButton")
    if (!$settingsClosedByComboEscape) {
        throw "Settings smoke: Escape did not close the Settings page from a ComboBox."
    }

    if (!$focusRestoredAfterComboEscape) {
        throw "Settings smoke: Escape from a ComboBox did not restore focus to the title-bar Settings button."
    }

    Open-SettingsFromGear $mainWindow.Handle
    Select-SettingsCategory $mainWindow.Handle "更新与维护"
    $settingsTextBox = Get-AutomationElementByNameAndControlType $mainWindow.Handle "更新源" "ControlType.Edit" $WaitSeconds
    $settingsTextBox.SetFocus()
    Start-Sleep -Milliseconds 300
    Send-EscapeToWindow $mainWindow.Handle
    $settingsClosedByTextBoxEscape = -not (Test-SettingsPageVisible $mainWindow.Handle)
    $focusedAfterTextBoxEscape = [System.Windows.Automation.AutomationElement]::FocusedElement
    $focusRestoredAfterTextBoxEscape = ($focusedAfterTextBoxEscape -ne $null) -and ($focusedAfterTextBoxEscape.Current.AutomationId -eq "SettingsTitleBarButton")
    if (!$settingsClosedByTextBoxEscape) {
        throw "Settings smoke: Escape did not close the Settings page from a TextBox."
    }

    if (!$focusRestoredAfterTextBoxEscape) {
        throw "Settings smoke: Escape from a TextBox did not restore focus to the title-bar Settings button."
    }

    # After a ContentDialog closes, focus returns inside Settings and Escape
    # must still close the page rather than being swallowed by the dialog host.
    Open-SettingsFromGear $mainWindow.Handle
    $aboutButton = Get-AutomationElementByName $mainWindow.Handle "关于" $WaitSeconds
    $aboutButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 900
    Send-EscapeToWindow $mainWindow.Handle
    $settingsOpenAfterDialogEscape = Test-SettingsPageVisible $mainWindow.Handle
    if (!$settingsOpenAfterDialogEscape) {
        throw "Settings smoke: the About dialog did not close before Settings, or the dialog did not open."
    }

    Send-EscapeToWindow $mainWindow.Handle
    $settingsClosedAfterDialogEscape = -not (Test-SettingsPageVisible $mainWindow.Handle)
    $focusedAfterDialogEscape = [System.Windows.Automation.AutomationElement]::FocusedElement
    $focusRestoredAfterDialogEscape = ($focusedAfterDialogEscape -ne $null) -and ($focusedAfterDialogEscape.Current.AutomationId -eq "SettingsTitleBarButton")
    if (!$settingsClosedAfterDialogEscape) {
        throw "Settings smoke: Escape did not close the Settings page after the About dialog closed."
    }

    if (!$focusRestoredAfterDialogEscape) {
        throw "Settings smoke: Escape after the About dialog did not restore focus to the title-bar Settings button."
    }

    $stuckEscAfterAllEscape = Test-EscapePopupVisible $mainWindow.Handle
    if ($stuckEscAfterAllEscape) {
        throw "Settings smoke: a stray 'Esc' accelerator popup remained after closing Settings."
    }
    $settingsJson = Get-Content -Path $settingsPath -Raw | ConvertFrom-Json
    if ($settingsJson.SettingsVersion -ne 10) {
        throw "Settings smoke failed: SettingsVersion was '$($settingsJson.SettingsVersion)', expected '10'."
    }

    $removedShortcutSettings = @(
        "EnableGlobalHotkeys",
        "EnableQuickActionsShortcut",
        "SelectedQuickActionsShortcutIndex"
    )
    foreach ($propertyName in $removedShortcutSettings) {
        if ($settingsJson.PSObject.Properties.Name -contains $propertyName) {
            throw "Settings smoke failed: removed shortcut setting '$propertyName' is still persisted."
        }
    }

    if ($settingsJson.UseProxyForUpdates -ne $true) {
        throw "Settings smoke failed: UseProxyForUpdates was '$($settingsJson.UseProxyForUpdates)', expected 'True'."
    }

    $resultPath = Join-Path $outputFull "settings-smoke-$timestamp.json"
    [pscustomobject]@{
        AppPath = $appFull
        SettingsDirectory = $settingsDirectory
        SettingsPath = $settingsPath
        ProcessId = $process.Id
        MainWindowTitle = $mainWindow.Title
        SettingsTitle = $settingsTitle
        SettingsSearchBox = $settingsSearchBox
        ThemeControl = $themeControl
        BackdropControl = $backdropControl
        PoetryToggle = $poetryToggle
        PoetryOptionsExpander = $poetryOptionsExpander
        PoetryServiceControl = $poetryServiceControl
        PoetryLanguageControl = $poetryLanguageControl
        DefaultsSection = $defaultsSection
        OpenThresholdSettingsButton = $openThresholdSettingsButton
        OpenDetectionRulesButton = $openDetectionRulesButton
        ThresholdsSection = $thresholdsSection
        ThresholdConfigControl = $thresholdConfigControl
        BrowseThresholdConfigButton = $browseThresholdConfigButton
        RulesSection = $rulesSection
        ReportsSection = $reportsSection
        LogsSection = $logsSection
        VoltageMinThreshold = $voltageMinThreshold
        VoltageMaxThreshold = $voltageMaxThreshold
        CurrentMaxThreshold = $currentMaxThreshold
        PowerFactorMinThreshold = $powerFactorMinThreshold
        TemperatureMaxThreshold = $temperatureMaxThreshold
        FreezeCountThreshold = $freezeCountThreshold
        MoreThresholdsExpander = $moreThresholdsExpander
        CurrentUnbalanceMaxThreshold = $currentUnbalanceMaxThreshold
        ActivePowerMinThreshold = $activePowerMinThreshold
        TemperatureMinThreshold = $temperatureMinThreshold
        CurrentActiveMinThreshold = $currentActiveMinThreshold
        FreezeStdThreshold = $freezeStdThreshold
        VoltageImbalanceThreshold = $voltageImbalanceThreshold
        VoltageUnitHint = $voltageUnitHint
        CurrentUnitHint = $currentUnitHint
        RatioUnitHint = $ratioUnitHint
        SampleCountHint = $sampleCountHint
        CurrentOverloadRule = $currentOverloadRule
        CurrentUnbalanceRule = $currentUnbalanceRule
        PowerFactorRule = $powerFactorRule
        DetailOutputRule = $detailOutputRule
        DesktopNotificationToggle = $desktopNotificationToggle
        DesktopNotificationTestButton = $desktopNotificationTestButton
        OpenWindowsNotificationSettingsButton = $openWindowsNotificationSettingsButton
        StartupTrayToggle = $startupTrayToggle
        AutoStartToggle = $autoStartToggle
        OpenWindowsStartupSettingsButton = $openWindowsStartupSettingsButton
        LlmSection = $llmSection
        LlmConnectionExpander = $llmConnectionExpander
        LlmEndpointControl = $llmEndpointControl
        LlmProxyToggle = $llmProxyToggle
        LlmTestButton = $llmTestButton
        NtfySection = $ntfySection
        NtfySettingsExpander = $ntfySettingsExpander
        NtfyServerControl = $ntfyServerControl
        NtfyProxyToggle = $ntfyProxyToggle
        NtfyTestButton = $ntfyTestButton
        DiagnosticsSection = $diagnosticsSection
        DiagnosticsExpander = $diagnosticsExpander
        ProxySection = $proxySection
        ProxyAddressControl = $proxyAddressControl
        ProxyTestButton = $proxyTestButton
        OpenWindowsProxySettingsButton = $openWindowsProxySettingsButton
        UpdatesSection = $updatesSection
        AdvancedUpdateExpander = $advancedUpdateExpander
        UpdateStatusText = $updateStatusText
        UpdateStatusVisibleText = $updateStatusVisibleText
        UpdateFullVersionHash = $fullVersionHash
        UpdateShortVersionHash = $shortVersionHash
        UpdateProxyToggle = $updateProxyToggle
        CheckUpdatesButton = $checkUpdatesButton
        OpenUpdatePageButton = $openUpdatePageButton
        UpdateFeedControl = $updateFeedControl
        RemovedShortcutSettingsPersisted = @($removedShortcutSettings | Where-Object { $settingsJson.PSObject.Properties.Name -contains $_ })
        SettingsVersion = $settingsJson.SettingsVersion
        SkipStartupIntegrationMutation = [bool]$SkipStartupIntegrationMutation
        ThemeIndex = $ThemeIndex
        DesktopHealthSection = $desktopHealthSection
        RecentLimitControl = $recentLimitControl
        LogLimitControl = $logLimitControl
        SaveSettingsButton = $saveSettingsButton
        ResetSettingsButton = $resetSettingsButton
        SettingsNavigationNames = $settingsNavigationNames
        SettingsBackButtonBounds = $backBounds
        SettingsBackButtonIsTopLeft = $backIsTopLeft
        SettingsSearchVisible = $settingsSearchVisible
        SettingsSearchNoResultsVisible = $settingsSearchNoResultsVisible
        SettingsSearchNoResultsCleared = $settingsSearchNoResultsCleared
        SettingsSearchNavigationFocus = $settingsSearchNavigationFocus
        SettingsFeedbackVisibleBeforeNavigation = $settingsFeedbackVisible
        SettingsFeedbackClearedOnNavigation = $settingsFeedbackClearedOnNavigation
        WorkbenchAccessibleNames = $workbenchAccessibleNames
        SettingsSaveVisible = $settingsSaveVisible
        SettingsResetVisible = $settingsResetVisible
        SettingsClosedBySearchEscape = $settingsClosedBySearchEscape
        FocusRestoredAfterSearchEscape = $focusRestoredAfterSearchEscape
        StuckEscapePopupBeforeSearchEscape = $stuckEscBeforeSearchEscape
        StuckEscapePopupAfterSearchEscape = $stuckEscAfterSearchEscape
        CtrlCommaTested = $ctrlCommaTested
        CtrlCommaOpenedSettings = $ctrlCommaOpenedSettings
        AltLeftTested = $altLeftTested
        AltLeftClosedSettings = $altLeftClosedSettings
        SettingsClosedByEscape = $settingsClosedByEscape
        FocusRestoredAfterEscape = $focusRestoredAfterEscape
        SettingsClosedByComboEscape = $settingsClosedByComboEscape
        FocusRestoredAfterComboEscape = $focusRestoredAfterComboEscape
        SettingsClosedByTextBoxEscape = $settingsClosedByTextBoxEscape
        FocusRestoredAfterTextBoxEscape = $focusRestoredAfterTextBoxEscape
        SettingsOpenAfterDialogEscape = $settingsOpenAfterDialogEscape
        SettingsClosedAfterDialogEscape = $settingsClosedAfterDialogEscape
        FocusRestoredAfterDialogEscape = $focusRestoredAfterDialogEscape
        StuckEscapePopupAfterAllEscape = $stuckEscAfterAllEscape
        Screenshots = $settingsScreenshots
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
        ResponsiveMode = if ($actualClientWidthDip -lt 1080) { "compact" } else { "comfortable" }
        Trigger = "Title bar settings button after writing persisted settings"
        Status = if ($sizeClamped) { "Skipped-ClampedRequestedSize" } else { "Passed" }
        Passed = -not $sizeClamped
        CapturedAt = (Get-Date).ToString("o")
    } | ConvertTo-Json -Depth 6 | Set-Content -Path $resultPath -Encoding UTF8

    if ($sizeClamped) {
        $clampDetail = "Requested $($Width)x$($Height) DIP produced a $([Math]::Round($actualClientWidthDip, 1))x$([Math]::Round($actualClientHeightDip, 1)) DIP client area ($($clampReasons -join '; '))."
        if (!$AllowClampedSize) {
            throw "Settings smoke invalid for the requested size: $clampDetail Re-run with -AllowClampedSize only for clamped-size evidence."
        }

        Write-Warning "SKIPPED requested-size verification: $clampDetail Checks passed at the clamped size only."
        Write-Host "Settings smoke clamped-size evidence captured at $([Math]::Round($actualClientWidthDip, 1))x$([Math]::Round($actualClientHeightDip, 1)) DIP client: $resultPath"
        return
    }

    Write-Host "Settings smoke passed at $([Math]::Round($actualClientWidthDip, 1))x$([Math]::Round($actualClientHeightDip, 1)) DIP client: $resultPath"
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

    if (!$SkipStartupIntegrationMutation) {
        Remove-StartupTask
        Restore-StartupTask $taskBackup

        if ($runEntryExisted) {
            New-Item -Path $runKeyPath -Force | Out-Null
            Set-ItemProperty -Path $runKeyPath -Name $runEntryName -Value $runEntryBackup
        }
        else {
            Remove-ItemProperty -Path $runKeyPath -Name $runEntryName -ErrorAction SilentlyContinue
        }
    }

    if ($null -eq $previousSettingsOverride) {
        Remove-Item -Path "Env:$settingsEnvironmentVariable" -ErrorAction SilentlyContinue
    }
    else {
        $env:EDETECTION_DESKTOP_SETTINGS_DIR = $previousSettingsOverride
    }
}

# schtasks.exe may return 1 when an optional startup task was absent during cleanup.
# Any actual test or restoration exception exits before reaching this line.
$global:LASTEXITCODE = 0
