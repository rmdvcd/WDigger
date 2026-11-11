#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Interactive, non-destructive startup validation for a disposable Windows VM.

.DESCRIPTION
  WDigger requires an elevated token by design. This script relaunches itself
  through UAC when needed, builds a Debug executable if -Executable is omitted,
  starts it briefly, and fails if the process exits or writes a new crash log.
  It does not drive the UI or execute any repair; use VALIDACION_VM_WINDOWS.md
  for the human VM matrix.
#>
[CmdletBinding()]
param(
    [string]$Executable,
    [ValidateRange(5, 120)]
    [int]$RunSeconds = 12,
    [switch]$KeepBuildOutput
)

$ErrorActionPreference = 'Stop'

function Test-ElevatedToken {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-ElevatedToken)) {
    $engine = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh.exe' } else { 'powershell.exe' }
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-RunSeconds', $RunSeconds)
    if (-not [string]::IsNullOrWhiteSpace($Executable)) { $arguments += @('-Executable', $Executable) }
    if ($KeepBuildOutput) { $arguments += '-KeepBuildOutput' }
    $argumentLine = ($arguments | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }) -join ' '
    $child = Start-Process -FilePath $engine -Verb RunAs -PassThru -Wait -ArgumentList $argumentLine
    exit $child.ExitCode
}

$output = $null
$process = $null
try {
    if ([string]::IsNullOrWhiteSpace($Executable)) {
        $project = Join-Path $PSScriptRoot 'src\WDigger\WDigger.csproj'
        $output = Join-Path $env:TEMP ('WDigger-startup-validation-' + [guid]::NewGuid().ToString('N'))
        dotnet build $project -c Debug -p:Platform=x64 -o $output
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
        $Executable = Join-Path $output 'WDigger.exe'
    }

    $Executable = [IO.Path]::GetFullPath($Executable)
    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw "No se encuentra $Executable" }
    if ([IO.Path]::GetExtension($Executable) -ne '.exe') { throw 'El ejecutable de validación debe terminar en .exe.' }

    $localCrash = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'WDigger\logs\WDigger.crash.log'
    $crashPaths = @(
        (Join-Path (Split-Path -Parent $Executable) 'WDigger.crash.log'),
        $localCrash
    )
    $crashBaseline = @{}
    foreach ($path in $crashPaths) {
        if (Test-Path -LiteralPath $path) { $crashBaseline[$path] = (Get-Item -LiteralPath $path).LastWriteTimeUtc }
    }

    $startedAt = [DateTime]::UtcNow
    $process = Start-Process -FilePath $Executable -PassThru
    if (-not $process.WaitForExit($RunSeconds * 1000)) {
        $process.Refresh()
    }
    if ($process.HasExited) { throw "La aplicación terminó durante el arranque: $($process.ExitCode)" }

    foreach ($path in $crashPaths) {
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $lastWrite = (Get-Item -LiteralPath $path).LastWriteTimeUtc
        $baseline = if ($crashBaseline.ContainsKey($path)) { $crashBaseline[$path] } else { [DateTime]::MinValue }
        if ($lastWrite -gt $baseline -and $lastWrite -ge $startedAt.AddSeconds(-2)) {
            throw "Se detectó un crash log nuevo durante el arranque: $path`n$(Get-Content -LiteralPath $path -Raw)"
        }
    }

    Write-Host "PASS: WDigger permaneció activo $RunSeconds segundos con token elevado y sin crash log nuevo."
    Write-Host "Ejecutable validado: $Executable"
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    if ($output -and -not $KeepBuildOutput -and (Test-Path -LiteralPath $output)) {
        Remove-Item -LiteralPath $output -Recurse -Force
    }
}
