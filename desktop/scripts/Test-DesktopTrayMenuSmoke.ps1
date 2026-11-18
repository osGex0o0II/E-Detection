[CmdletBinding()]
param(
    [string]$PackagePath = "",

    [string]$SourceRoot = "",

    [string]$CoreRoot = "",

    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")

if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $PackagePath = if (Test-Path (Join-Path $scriptDir "EDetection.exe")) {
        $scriptDir
    }
    else {
        Join-Path $repoRoot "artifacts\desktop\win-x64\publish"
    }
}

if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $ancestorRoot = Resolve-Path (Join-Path $scriptDir "..\..\..") -ErrorAction SilentlyContinue
    $sourceCandidates = @(
        (Join-Path (Get-Location).Path "desktop\EDetection.Desktop"),
        (Join-Path $repoRoot "desktop\EDetection.Desktop"),
        $(if ($ancestorRoot) { Join-Path $ancestorRoot.Path "desktop\EDetection.Desktop" })
    )
    $SourceRoot = ($sourceCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1)
}

if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    throw "Tray-menu smoke failed: could not locate desktop source root. Pass -SourceRoot explicitly."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = if (Test-Path (Join-Path $scriptDir "EDetection.exe")) {
        Join-Path $scriptDir "smoke-results\tray-menu"
    }
    else {
        Join-Path $repoRoot "artifacts\desktop\tray-menu-smoke"
    }
}

$packageFull = [System.IO.Path]::GetFullPath((Resolve-Path $PackagePath).Path)
$sourceFull = [System.IO.Path]::GetFullPath((Resolve-Path $SourceRoot).Path)
$outputFull = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputFull | Out-Null

# TrayIconService moved to the WinUI-free EDetection.Desktop.Core assembly;
# MainWindow stays in the shell, so both roots are inspected.
if ([string]::IsNullOrWhiteSpace($CoreRoot)) {
    $CoreRoot = Join-Path (Split-Path -Parent $sourceFull) "EDetection.Desktop.Core"
}

$coreFull = [System.IO.Path]::GetFullPath($CoreRoot)
$trayServicePath = Join-Path $coreFull "Services\TrayIconService.cs"
$mainWindowPath = Join-Path $sourceFull "MainWindow.xaml.cs"
if (!(Test-Path $trayServicePath) -or !(Test-Path $mainWindowPath)) {
    throw "Tray-menu smoke failed: source files were not found under $sourceFull / $coreFull"
}

$traySource = Get-Content -LiteralPath $trayServicePath -Raw
$mainWindowSource = Get-Content -LiteralPath $mainWindowPath -Raw
$requiredTrayTokens = @(
    "开始检测",
    "取消检测",
    "打开最新报告",
    "打开报告目录",
    "ResolveCurrentIconHandle",
    "StartRequested",
    "CancelRequested",
    "OpenReportRequested",
    "OpenReportFolderRequested"
)

$requiredMainWindowTokens = @(
    "ViewModel.StartCommand",
    "ViewModel.CancelCommand",
    "ViewModel.OpenCurrentReportCommand",
    "ViewModel.OpenCurrentReportFolderCommand",
    "running.ico",
    "UpdateWindowIcon",
    "UpdateTrayCommands"
)

$missing = @()
foreach ($token in $requiredTrayTokens) {
    if ($traySource.IndexOf($token, [System.StringComparison]::Ordinal) -lt 0) {
        $missing += "TrayIconService:$token"
    }
}

foreach ($token in $requiredMainWindowTokens) {
    if ($mainWindowSource.IndexOf($token, [System.StringComparison]::Ordinal) -lt 0) {
        $missing += "MainWindow:$token"
    }
}

$entryPoint = Join-Path $packageFull "EDetection.exe"
if (!(Test-Path $entryPoint)) {
    $missing += "Package:EDetection.exe"
}

$runningIcon = Join-Path $packageFull "Assets\Icons\running.ico"
if (!(Test-Path $runningIcon)) {
    $missing += "Package:Assets\\Icons\\running.ico"
}

if ($missing.Count -gt 0) {
    throw "Tray-menu smoke failed: missing $($missing -join ', ')"
}

# The running icon must actually read as "running" in the notification area.
# The previous artwork differed from the idle icon by six pixels at 16 px,
# which looked unchanged; compare the source icons so this stays true even
# when the publish folder is stale.
$sourceIdleIcon = Join-Path $sourceFull "Assets\Icons\app.ico"
$sourceRunningIcon = Join-Path $sourceFull "Assets\Icons\running.ico"
$iconDistinctness = $null
if ((Test-Path $sourceIdleIcon) -and (Test-Path $sourceRunningIcon)) {
    Add-Type -AssemblyName System.Drawing -ErrorAction Stop
    $idleIcon = New-Object System.Drawing.Icon($sourceIdleIcon, 16, 16)
    $runIcon = New-Object System.Drawing.Icon($sourceRunningIcon, 16, 16)
    $idleBitmap = $idleIcon.ToBitmap()
    $runBitmap = $runIcon.ToBitmap()
    $differentPixels = 0
    for ($y = 0; $y -lt $idleBitmap.Height; $y++) {
        for ($x = 0; $x -lt $idleBitmap.Width; $x++) {
            $idlePixel = $idleBitmap.GetPixel($x, $y)
            $runPixel = $runBitmap.GetPixel($x, $y)
            $delta = [Math]::Abs([int]$idlePixel.R - [int]$runPixel.R) +
                     [Math]::Abs([int]$idlePixel.G - [int]$runPixel.G) +
                     [Math]::Abs([int]$idlePixel.B - [int]$runPixel.B)
            if ($delta -gt 30) { $differentPixels++ }
        }
    }
    $iconPixels = $idleBitmap.Width * $idleBitmap.Height
    $idleBitmap.Dispose(); $runBitmap.Dispose(); $idleIcon.Dispose(); $runIcon.Dispose()
    $iconDistinctness = [Math]::Round($differentPixels / $iconPixels, 3)
    if ($iconDistinctness -lt 0.08) {
        throw "Tray-menu smoke failed: running.ico differs from app.ico in only $differentPixels/$iconPixels pixels at 16 px."
    }
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$resultPath = Join-Path $outputFull "tray-menu-smoke-$timestamp.json"
[pscustomobject]@{
    PackagePath = $packageFull
    SourceRoot = $sourceFull
    Commands = @("开始检测", "取消检测", "打开最新报告", "打开报告目录")
    RunningIcon = $runningIcon
    RunningIconDistinctness = $iconDistinctness
    Passed = $true
    CapturedAt = (Get-Date).ToString("o")
} | ConvertTo-Json | Set-Content -Path $resultPath -Encoding UTF8

Write-Host "Tray-menu smoke passed: $resultPath"
