param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot "TrayApp.csproj"
$publishProfile = "WinX64SelfContained"
$publishDir = Join-Path $projectRoot "artifacts\publish\win-x64"

Write-Host "Publishing AI Assistent ($Configuration, win-x64, self-contained)..." -ForegroundColor Cyan

dotnet publish $projectFile -c $Configuration -f net10.0-windows -p:PublishProfile=$publishProfile
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

Write-Host "Publish output ready: $publishDir" -ForegroundColor Green
