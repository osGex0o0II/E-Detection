# Shared Win32 window and UI Automation helpers for the desktop smoke tests.
#
# Dot-source this file from a smoke script after $scriptDir is resolved:
#   . (Join-Path $scriptDir "DesktopUiAutomation.ps1")
#
# This is a dot-sourced .ps1 rather than a .psm1 so the published package can
# keep the smoke scripts working from their own directory: Publish-Desktop.ps1
# copies this file next to them and no module path lookup is required.

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if ($null -eq ('DesktopUiAutomationNative' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class DesktopUiAutomationNative
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
}
"@
}

function Get-WindowTitle {
    param([IntPtr]$Handle)

    $builder = [System.Text.StringBuilder]::new(512)
    [DesktopUiAutomationNative]::GetWindowText($Handle, $builder, $builder.Capacity) | Out-Null
    $builder.ToString()
}

function Get-ProcessWindows {
    param([int]$ProcessId)

    $windows = [System.Collections.Generic.List[object]]::new()
    $callback = [DesktopUiAutomationNative+EnumWindowsProc]{
        param([IntPtr]$hWnd, [IntPtr]$lParam)
        $windowProcessId = 0
        [DesktopUiAutomationNative]::GetWindowThreadProcessId($hWnd, [ref]$windowProcessId) | Out-Null
        if ($windowProcessId -eq $ProcessId -and [DesktopUiAutomationNative]::IsWindowVisible($hWnd)) {
            $title = Get-WindowTitle $hWnd
            if (![string]::IsNullOrWhiteSpace($title)) {
                $windows.Add([pscustomobject]@{
                    Handle = $hWnd
                    Title = $title
                })
            }
        }

        return $true
    }

    [DesktopUiAutomationNative]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null
    $windows
}

function Wait-ForWindowTitle {
    param(
        [int]$ProcessId,
        [string]$Pattern,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $match = Get-ProcessWindows $ProcessId | Where-Object { $_.Title -like $Pattern } | Select-Object -First 1
        if ($match -ne $null) {
            return $match
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for window title pattern '$Pattern'."
}

function Wait-ForAutomationElement {
    param(
        [IntPtr]$RootHandle,
        [string]$Name,
        [int]$TimeoutSeconds
    )

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

function Find-AutomationElement {
    param(
        [IntPtr]$RootHandle,
        [string]$Name,
        [int]$TimeoutSeconds
    )

    Wait-ForAutomationElement $RootHandle $Name $TimeoutSeconds
}

function Wait-ForAutomationName {
    param(
        [IntPtr]$RootHandle,
        [string]$Name,
        [int]$TimeoutSeconds
    )

    $element = Wait-ForAutomationElement $RootHandle $Name $TimeoutSeconds
    return $element.Current.Name
}

function Invoke-AutomationElement {
    param(
        [IntPtr]$RootHandle,
        [string]$Name,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($RootHandle)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)

    do {
        $match = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($match -ne $null) {
            $pattern = $match.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            $pattern.Invoke()
            return $match.Current.Name
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    return $null
}
