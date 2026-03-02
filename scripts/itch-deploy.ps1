param(
    # itch.io target in the form "user/game:channel", e.g. "nozgames/dominoz:html5"
    [Parameter(Mandatory)][string]$ItchTarget,
    [string]$ButlerPath = "butler"
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot/_web-helpers.ps1"

& "$PSScriptRoot/web-build.ps1" -ExtraArgs @("/p:WasmFingerprintDotNetJs=false", "/p:OverrideHtmlAssetPlaceholders=false")
if ($LASTEXITCODE -ne 0) { exit 1 }

$PublishDir = Get-WebPublishDir

Write-Host "`nPatching for itch.io..." -ForegroundColor Cyan
Invoke-ItchPatches $PublishDir

# Verify
Write-Host "`nVerification:" -ForegroundColor Cyan
$Check = Get-Content "$PublishDir/index.html" -Raw
if ($Check -match 'framework/blazor\.webassembly\.js') {
    Write-Host "  index.html -> framework/blazor.webassembly.js" -ForegroundColor Green
} else {
    Write-Host "  WARNING: blazor script reference not found in index.html" -ForegroundColor Red
}
if (Test-Path "$PublishDir/framework/blazor.webassembly.js") {
    Write-Host "  framework/blazor.webassembly.js exists" -ForegroundColor Green
} else {
    Write-Host "  WARNING: framework/blazor.webassembly.js not found!" -ForegroundColor Red
}

Write-Host "`nPushing to itch.io ($ItchTarget)..." -ForegroundColor Cyan
& $ButlerPath push $PublishDir $ItchTarget
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host "Done!" -ForegroundColor Green
