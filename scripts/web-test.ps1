param(
    [int]$Port = 8080,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot/_web-helpers.ps1"

if (-not $NoBuild) {
    & "$PSScriptRoot/web-build.ps1"
    if ($LASTEXITCODE -ne 0) { exit 1 }
}

$PublishDir = Get-WebPublishDir
Copy-FingerprintedFrameworkFiles $PublishDir

if (-not (Get-Command dotnet-serve -ErrorAction SilentlyContinue)) {
    Write-Host "Installing dotnet-serve..." -ForegroundColor Yellow
    dotnet tool install -g dotnet-serve
}

Write-Host "`nServing at: http://localhost:$Port/" -ForegroundColor Cyan
Write-Host "Press Ctrl+C to stop`n" -ForegroundColor Yellow

dotnet-serve -p $Port -d $PublishDir --quiet
