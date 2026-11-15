#requires -Version 7.4
[CmdletBinding()]
param(
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root

$failures = [System.Collections.Generic.List[string]]::new()

function Add-Failure {
    param([string]$Message)
    $failures.Add($Message)
    Write-Output "FAIL: $Message"
}

function Require-File {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Add-Failure "missing file: $Path"
    }
}

Require-File 'Directory.Build.props'
Require-File 'Directory.Packages.props'
Require-File 'desktop/EDetection.Domain/EDetection.Domain.csproj'
Require-File 'desktop/EDetection.Infrastructure/EDetection.Infrastructure.csproj'
Require-File 'desktop/EDetection.Tests/EDetection.Tests.csproj'

$legacy = @(git ls-files build/E-Detection)
if ($legacy.Count -gt 0) {
    Add-Failure "legacy PyInstaller output is still tracked ($($legacy.Count) files)"
}

$versioned = @(
    Get-ChildItem -Path desktop -Filter *.csproj -Recurse |
        Select-String -Pattern 'PackageReference[^>]*\bVersion=' -List
)
if ($versioned.Count -gt 0) {
    $paths = ($versioned | ForEach-Object { $_.Path }) -join ', '
    Add-Failure "PackageReference still declares Version with central package management: $paths"
}

foreach ($project in @(
    'desktop/EDetection.Domain/EDetection.Domain.csproj',
    'desktop/EDetection.Desktop.Core/EDetection.Desktop.Core.csproj'
)) {
    $content = Get-Content -LiteralPath $project -Raw
    if ($content -match 'Microsoft\.WindowsAppSDK|Microsoft\.WinUI|UseWinUI') {
        Add-Failure "$project must stay WinUI-free"
    }
}

$limits = @{
    'desktop/EDetection.Desktop/ViewModels/MainViewModel.cs' = 600
    'desktop/EDetection.NativeCore/Services/NativeDetectionBackendService.cs' = 120
}
foreach ($entry in $limits.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $entry.Key -PathType Leaf)) {
        Add-Failure "missing file: $($entry.Key)"
        continue
    }
    $count = (Get-Content -LiteralPath $entry.Key).Count
    if ($count -gt $entry.Value) {
        Add-Failure "$($entry.Key) has $count lines, limit is $($entry.Value)"
    }
}

$settingsPartials = @(
    Get-ChildItem -LiteralPath 'desktop/EDetection.Desktop/Views' -Filter 'SettingsView*.cs' -File
)
foreach ($partial in $settingsPartials) {
    $count = (Get-Content -LiteralPath $partial.FullName).Count
    if ($count -gt 450) {
        Add-Failure "$($partial.FullName) has $count lines, SettingsView partial limit is 450"
    }
}

$backendPartials = @(
    Get-ChildItem -LiteralPath 'desktop/EDetection.NativeCore/Services' -Filter 'NativeDetectionBackendService*.cs' -File
)
foreach ($partial in $backendPartials) {
    $count = (Get-Content -LiteralPath $partial.FullName).Count
    if ($count -gt 800) {
        Add-Failure "$($partial.FullName) has $count lines, NativeDetectionBackendService partial limit is 800"
    }
}

$viewModelPartials = @(
    Get-ChildItem -LiteralPath 'desktop/EDetection.Desktop/ViewModels' -Filter 'MainViewModel*.cs' -File
)
foreach ($partial in $viewModelPartials) {
    $count = (Get-Content -LiteralPath $partial.FullName).Count
    if ($count -gt 800) {
        Add-Failure "$($partial.FullName) has $count lines, MainViewModel partial limit is 800"
    }
}

$buildSucceeded = $true
if (-not $NoBuild) {
    Write-Output 'Building desktop solution...'
    dotnet build desktop\EDetection.Desktop.slnx -c Debug
    if ($LASTEXITCODE -ne 0) {
        Add-Failure 'dotnet build failed'
        $buildSucceeded = $false
    }
}

if ($buildSucceeded) {
    Write-Output 'Running xUnit v3 composition/architecture tests...'
    dotnet test desktop\EDetection.Tests\EDetection.Tests.csproj -c Debug --no-build
    if ($LASTEXITCODE -ne 0) {
        Add-Failure 'dotnet test failed'
    }
}

if ($failures.Count -gt 0) {
    Write-Output ''
    Write-Output "Architecture gate failed with $($failures.Count) issue(s)."
    exit 1
}

Write-Output ''
Write-Output 'Architecture gate passed.'
