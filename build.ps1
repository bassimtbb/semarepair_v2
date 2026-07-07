# Workaround: Docker BuildKit (Docker 25+) cannot be disabled and reads build
# contexts via WSL2's 9P file server, which returns only OneDrive reparse-point
# stubs (31B) instead of real file content. Fix: copy each build context to
# $env:TEMP (not OneDrive), build from there, then clean up.

param(
    [string[]]$Only   # Optionally build specific services: -Only chat-service,frontend
)

$ErrorActionPreference = "Stop"
$Root   = $PSScriptRoot
$Temp   = "$env:TEMP\semarepair-docker-build"

$AllServices = @(
    [pscustomobject]@{ Name = "ingestion";       Context = "services/ingestion";       Exclude = @("__pycache__") }
    [pscustomobject]@{ Name = "ingestion-resx";  Context = "services/ingestion-resx";  Exclude = @("__pycache__") }
    [pscustomobject]@{ Name = "vehicle-service"; Context = "services/vehicle";         Exclude = @("bin", "obj") }
    [pscustomobject]@{ Name = "search-service";  Context = "services/search";          Exclude = @("bin", "obj") }
    [pscustomobject]@{ Name = "chat-service";    Context = "services/chat";            Exclude = @("bin", "obj") }
    [pscustomobject]@{ Name = "frontend";        Context = "frontend";                 Exclude = @("node_modules", "dist", ".angular", ".vscode") }
)

$Services = if ($Only) { $AllServices | Where-Object { $Only -contains $_.Name } } else { $AllServices }

if (Test-Path $Temp) { Remove-Item -Recurse -Force $Temp }
New-Item -ItemType Directory -Force $Temp | Out-Null

foreach ($svc in $Services) {
    $tag     = "semarepair_v2-$($svc.Name)"
    $src     = Join-Path $Root $svc.Context
    $tempDir = Join-Path $Temp $svc.Name

    Write-Host "`n>> $tag" -ForegroundColor Cyan

    # Copy to non-OneDrive temp (triggers OneDrive download on Windows side;
    # Docker BuildKit will then read from $env:TEMP which is always local)
    $rcArgs = @($src, $tempDir, "/E", "/NFL", "/NDL", "/NJH", "/NJS", "/NP")
    if ($svc.Exclude.Count -gt 0) { $rcArgs += @("/XD") + $svc.Exclude }
    robocopy @rcArgs | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $($svc.Name) (exit $LASTEXITCODE)" }

    docker build -t $tag $tempDir
    if ($LASTEXITCODE -ne 0) { throw "docker build failed for $tag" }

    Remove-Item -Recurse -Force $tempDir
    Write-Host "   $tag built OK" -ForegroundColor Green
}

Remove-Item -Recurse -Force $Temp -ErrorAction SilentlyContinue

Write-Host "`n>> docker compose up -d" -ForegroundColor Cyan
Set-Location $Root
docker compose up -d
if ($LASTEXITCODE -ne 0) { throw "docker compose up -d failed" }
Write-Host "`nAll services started." -ForegroundColor Green
