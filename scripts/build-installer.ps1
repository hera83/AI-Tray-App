param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [switch]$SkipPublish,
    [string]$AppVersion,
    [string]$SignToolCmd
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot "TrayApp.csproj"
$publishScript = Join-Path $projectRoot "scripts\publish-win-x64.ps1"
$installerScript = Join-Path $projectRoot "scripts\installer\TrayApp.iss"
$publishExe = Join-Path $projectRoot "artifacts\publish\win-x64\AIAssistent.exe"
$installerOutputDir = Join-Path $projectRoot "artifacts\installer"

if (-not $SkipPublish) {
    & $publishScript -Configuration $Configuration
}

if (-not (Test-Path $publishExe)) {
    throw "Publish output not found at '$publishExe'. Run scripts\\publish-win-x64.ps1 first."
}

[string]$resolvedAppVersion = $AppVersion
if ([string]::IsNullOrWhiteSpace($resolvedAppVersion)) {
    [xml]$csproj = Get-Content $projectFile
    $resolvedAppVersion = $csproj.Project.PropertyGroup.Version | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($resolvedAppVersion)) {
    $resolvedAppVersion = "1.0.0"
}

$isccCandidates = @()
if ($env:ProgramFiles) {
    $isccCandidates += (Join-Path $env:ProgramFiles "Inno Setup 6\\ISCC.exe")
}
if (${env:ProgramFiles(x86)}) {
    $isccCandidates += (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\\ISCC.exe")
}
if ($env:LOCALAPPDATA) {
    $isccCandidates += (Join-Path $env:LOCALAPPDATA "Programs\\Inno Setup 6\\ISCC.exe")
}

$isccPath = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $isccPath) {
    throw "Inno Setup compiler not found. Install Inno Setup 6 (ISCC.exe), then run this script again."
}

Write-Host "Building installer with Inno Setup..." -ForegroundColor Cyan
$isccArgs = @(
    "/DAppVersion=$resolvedAppVersion"
)

if (-not [string]::IsNullOrWhiteSpace($SignToolCmd)) {
    $isccArgs += "/DSignToolCmd=$SignToolCmd"
}

$isccArgs += $installerScript

& $isccPath @isccArgs
if ($LASTEXITCODE -ne 0) {
    throw "Installer build failed."
}

Write-Host "Installer output ready: $installerOutputDir" -ForegroundColor Green
