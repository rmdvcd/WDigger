#!/usr/bin/env pwsh
# CI check: WDigger must start without XamlParseException / crash log
$project = Resolve-Path -Path "$PWD/src/WDigger"
$csproj = Join-Path $project "WDigger.csproj"

# Build (Debug) x64
Write-Host "=== Building WDigger (Debug x64) ==="
dotnet build $csproj -c Debug -p:Platform=x64 | Out-Null

# La app se construye en esta ruta conocida tras el build:
$exe = "src/WDigger/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/WDigger.exe"
if (-not (Test-Path $exe)) {
    Write-Error "No se encuentra WDigger.exe en la ruta esperada: $exe"
    exit 1
}
$crashLog = $exe + ".crashLog"

# Clean any stale crash log
if (Test-Path $crashLog) {
    Remove-Item $crashLog
}

# Run the app; if crash log appears, fail
Write-Host "=== Running WDigger.exe ==="
& $exe 2>$null
Start-Sleep -Seconds 12

if (Test-Path $crashLog) {
    Write-Error "FAIL: WDigger.crashLog was creado — app crashed on startup (probable XamlParseException)."
    Get-Content $crashLog
    exit 1
}

Write-Host "PASS: WDigger empezó sin generar crash log."
exit 0