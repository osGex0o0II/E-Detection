[CmdletBinding()]
param(
    [string]$ProjectPath = "",
    [string]$CorePath = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot "desktop\EDetection.Desktop"
}
# The WinUI-free services live in EDetection.Desktop.Core; the view models stay
# in the shell project, so both roots are inspected.
if ([string]::IsNullOrWhiteSpace($CorePath)) {
    $CorePath = Join-Path $repoRoot "desktop\EDetection.Desktop.Core"
}

$projectFull = [System.IO.Path]::GetFullPath((Resolve-Path $ProjectPath).Path)
$coreFull = [System.IO.Path]::GetFullPath((Resolve-Path $CorePath).Path)
$redactorPath = Join-Path $coreFull "Services\DiagnosticsRedactor.cs"
if (!(Test-Path $redactorPath)) {
    throw "Diagnostics redaction smoke failed: DiagnosticsRedactor.cs was not found at $redactorPath"
}

$source = Get-Content -Path $redactorPath -Raw
$checks = @(
    'UrlCredentialPattern',
    '%USERPROFILE%',
    '%LOCALAPPDATA%',
    '%APPDATA%',
    '%TEMP%',
    '{app}',
    '{localPath}'
)

foreach ($check in $checks) {
    if ($source.IndexOf($check, [System.StringComparison]::Ordinal) -lt 0) {
        throw "Diagnostics redaction smoke failed: expected redaction token '$check' was not found."
    }
}

$diagnosticsViewModelPath = Join-Path $projectFull "ViewModels\DiagnosticsViewModel.cs"
$diagnosticsViewModel = Get-Content -Path $diagnosticsViewModelPath -Raw
if ($diagnosticsViewModel.IndexOf("诊断信息: 已脱敏本机路径和凭据", [System.StringComparison]::Ordinal) -lt 0 `
    -or $diagnosticsViewModel.IndexOf("DiagnosticsRedactor.Redact", [System.StringComparison]::Ordinal) -lt 0) {
    throw "Diagnostics redaction smoke failed: diagnostics clipboard text is not explicitly redacted."
}

$runtimeLogServicePath = Join-Path $coreFull "Services\RuntimeLogService.cs"
$runtimeLogService = Get-Content -Path $runtimeLogServicePath -Raw
if ($runtimeLogService.IndexOf("DiagnosticsRedactor.Redact(item.Message)", [System.StringComparison]::Ordinal) -lt 0 `
    -or $runtimeLogService.IndexOf("说明\t诊断信息\t已脱敏本机路径和凭据", [System.StringComparison]::Ordinal) -lt 0) {
    throw "Diagnostics redaction smoke failed: runtime log export is not explicitly redacted."
}

Write-Host "Diagnostics redaction smoke passed: $projectFull"
