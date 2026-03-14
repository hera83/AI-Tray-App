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
    $resolvedAppVersion = "1.1.0"
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
New-Item -ItemType Directory -Force -Path $installerOutputDir | Out-Null

$outputBaseFilename = "AI Assistent-Setup-$resolvedAppVersion"
$stagingOutputDir = Join-Path $env:TEMP ("AI-Assistent-Installer-Staging-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $stagingOutputDir | Out-Null

$stagedInstaller = Join-Path $stagingOutputDir "$outputBaseFilename.exe"
$finalInstaller = Join-Path $installerOutputDir "$outputBaseFilename.exe"

$isccArgs = @(
    "/Qp",
    "/O$stagingOutputDir",
    "/F$outputBaseFilename",
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

if (-not (Test-Path $stagedInstaller)) {
    throw "Installer output not found at '$stagedInstaller'."
}

if (Test-Path $finalInstaller) {
    Remove-Item -LiteralPath $finalInstaller -Force
}

Copy-Item -LiteralPath $stagedInstaller -Destination $finalInstaller -Force
Remove-Item -LiteralPath $stagedInstaller -Force
Remove-Item -LiteralPath $stagingOutputDir -Force

Write-Host "Installer output ready: $finalInstaller" -ForegroundColor Green
