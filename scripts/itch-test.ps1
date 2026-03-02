param(
    [int]$Port = 8080,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot/_web-helpers.ps1"

if (-not $NoBuild) {
    & "$PSScriptRoot/web-build.ps1" -ExtraArgs @("/p:WasmFingerprintDotNetJs=false", "/p:OverrideHtmlAssetPlaceholders=false")
    if ($LASTEXITCODE -ne 0) { exit 1 }
}

$PublishDir = Get-WebPublishDir

Write-Host "`nPatching for itch.io simulation..." -ForegroundColor Cyan
Invoke-ItchPatches $PublishDir

# Mirror itch.io's subpath hosting at /html/<id>/
$TempDir = "$env:TEMP/itch-test"
if (Test-Path $TempDir) { Remove-Item $TempDir -Recurse -Force }
New-Item -ItemType Directory -Path "$TempDir/html/test" -Force | Out-Null
Copy-Item "$PublishDir/*" "$TempDir/html/test/" -Recurse

Write-Host "`nServing at: http://localhost:$Port/html/test/" -ForegroundColor Cyan
Write-Host "Simulates itch.io hosting at https://html.itch.zone/html/<id>/" -ForegroundColor Gray
Write-Host "Press Ctrl+C to stop`n" -ForegroundColor Yellow

python -m http.server $Port --directory $TempDir 2>$null
