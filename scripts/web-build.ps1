param(
    [string[]]$ExtraArgs = @()
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot/_web-helpers.ps1"

$WebProj   = Get-WebProject
$PublishDir = (Get-WebPublishDir)
$PublishRoot = $PublishDir -replace '/wwwroot$', ''

# Clean stale publish output
if (Test-Path $PublishRoot) {
    Remove-Item $PublishRoot -Recurse -Force
    Write-Host "Cleaned previous publish output" -ForegroundColor Yellow
}

Write-Host "Publishing web build (AOT)..." -ForegroundColor Cyan
dotnet publish $WebProj.FullName -c Release @ExtraArgs
if ($LASTEXITCODE -ne 0) { exit 1 }

$FileCount = (Get-ChildItem $PublishDir -Recurse -File).Count
$Size      = "{0:N1} MB" -f ((Get-ChildItem $PublishDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB)
Write-Host "`nPublish complete: $FileCount files, $Size" -ForegroundColor Green
Write-Host "Output: $PublishDir" -ForegroundColor Gray
