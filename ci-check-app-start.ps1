#!/usr/bin/env pwsh
# Optional interactive Windows smoke test. This is not a substitute for UI tests.
$ErrorActionPreference = 'Stop'

# The executable has a requireAdministrator manifest. Relaunch this optional
# interactive smoke test elevated so it can verify the real startup path without
# leaving an unattended UAC prompt behind.
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $engine = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh.exe' } else { 'powershell.exe' }
    $child = Start-Process -FilePath $engine -Verb RunAs -PassThru -Wait -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath))
    exit $child.ExitCode
}

$project = Join-Path $PSScriptRoot 'src/WDigger/WDigger.csproj'
$output = Join-Path $env:TEMP ('WDigger-smoke-' + [guid]::NewGuid().ToString('N'))
$process = $null
try {
    dotnet build $project -c Debug -p:Platform=x64 -o $output
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
    $exe = Join-Path $output 'WDigger.exe'
    $crashLog = Join-Path $output 'WDigger.crash.log'
    if (-not (Test-Path $exe)) { throw "No se encuentra $exe" }
    $process = Start-Process -FilePath $exe -PassThru
    Start-Sleep -Seconds 12
    $process.Refresh()
    if (Test-Path $crashLog) { throw "Se encontró un registro de fallo: $(Get-Content $crashLog -Raw)" }
    if ($process.HasExited) { throw "La aplicación terminó durante el arranque: $($process.ExitCode)" }
    Write-Host 'PASS: el proceso sigue activo y no se generó un crash log.'
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    if (Test-Path $output) { Remove-Item $output -Recurse -Force }
}
