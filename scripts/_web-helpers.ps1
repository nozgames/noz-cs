# Internal helpers for web build scripts — dot-sourced, not called directly.

function Get-WebProject {
    $proj = Get-Item "platform/web/*.csproj" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $proj) { throw "No .csproj found in platform/web/ — run from your project root" }
    return $proj
}

function Get-WebPublishDir {
    $proj = Get-WebProject
    $xml  = [xml](Get-Content $proj.FullName)
    $tfm  = ($xml.Project.PropertyGroup | Where-Object { $_.TargetFramework } | Select-Object -First 1).TargetFramework
    if (-not $tfm) { $tfm = "net10.0" }
    return "$($proj.DirectoryName -replace '\\','/')/bin/Release/$tfm/publish/wwwroot"
}

# Mirrors the Dockerfile's file-copy step: blazor.webassembly.HASH.js -> blazor.webassembly.js
# UseBlazorFrameworkFiles() doesn't do this mapping without a project reference to the WASM client.
function Copy-FingerprintedFrameworkFiles([string]$PublishDir) {
    $frameworkDir = "$PublishDir/_framework"
    if (-not (Test-Path $frameworkDir)) { return }
    Get-ChildItem $frameworkDir -File | Where-Object { $_.Extension -in '.js','.wasm' } | ForEach-Object {
        $base = $_.Name -replace '\.[a-z0-9]{6,}(\.(js|wasm))$', '$1'
        if ($base -ne $_.Name) {
            $dest = Join-Path $frameworkDir $base
            if (-not (Test-Path $dest)) { Copy-Item $_.FullName $dest }
        }
    }
}

function Remove-ServiceWorker([string]$PublishDir) {
    Remove-Item "$PublishDir/sw.js"        -ErrorAction SilentlyContinue
    Remove-Item "$PublishDir/manifest.json" -ErrorAction SilentlyContinue
    $IndexPath = "$PublishDir/index.html"
    $Html = Get-Content $IndexPath -Raw
    $Html = $Html -replace '(?m)^\s*<link rel="manifest"[^>]*>\r?\n', ''
    $Html = $Html -replace '(?s)\s*<script>\s*if \(''serviceWorker''.*?</script>', ''
    $Html | Set-Content $IndexPath -NoNewline
}

function Invoke-ItchPatches([string]$PublishDir) {
    # Remove pre-compressed files (static host, no server rewrites)
    Get-ChildItem $PublishDir -Recurse -Include "*.br","*.gz" | Remove-Item

    # Remove service worker and manifest (blocked in itch.io iframe)
    Remove-ServiceWorker $PublishDir

    # Rename _framework -> framework (itch.io may block underscore-prefixed dirs)
    $FrameworkDst = "$PublishDir/framework"
    if (Test-Path $FrameworkDst) { Remove-Item $FrameworkDst -Recurse -Force }
    Rename-Item "$PublishDir/_framework" "framework"
    Write-Host "  Renamed _framework -> framework" -ForegroundColor Gray

    # Fix _framework references in JS files
    Get-ChildItem "$FrameworkDst/*.js" | ForEach-Object {
        $content = Get-Content $_.FullName -Raw
        if ($content -match '_framework') {
            $content = $content -replace '_framework', 'framework'
            $content | Set-Content $_.FullName -NoNewline
            Write-Host "  Patched $($_.Name)" -ForegroundColor Gray
        }
    }

    # Patch index.html (base href + blazor script path; SW already removed above)
    $IndexPath = "$PublishDir/index.html"
    $Html = Get-Content $IndexPath -Raw
    $Html = $Html -replace '<base href="/" />',                                          '<base href="./" />'
    $Html = $Html -replace '<script src="[^"]*blazor\.webassembly[^"]*\.js"></script>', '<script src="framework/blazor.webassembly.js"></script>'
    $Html | Set-Content $IndexPath -NoNewline
    Write-Host "  Patched index.html" -ForegroundColor Gray
}
