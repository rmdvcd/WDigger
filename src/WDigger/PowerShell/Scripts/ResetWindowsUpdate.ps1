param()

$services = @('wuauserv', 'bits', 'cryptsvc')
$running = @()
foreach ($name in $services) {
    $service = Get-Service -Name $name -ErrorAction Stop
    if ($service.Status -notin @('Running', 'Stopped')) {
        throw "Servicio $name en estado transitorio; no se modifica nada."
    }
    if ($service.Status -eq 'Running') {
        $running += $name
    }
}

$directories = @(
    (Join-Path $env:windir 'SoftwareDistribution'),
    (Join-Path $env:windir 'System32\catroot2')
)
foreach ($path in $directories) {
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        throw "Falta la carpeta $path; no se modifica nada."
    }
    if ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Carpeta redirigida: $path; no se modifica nada."
    }
}

$renamed = @()
$failures = [System.Collections.Generic.List[string]]::new()
try {
    foreach ($name in $running) {
        Stop-Service -Name $name -ErrorAction Stop
        (Get-Service -Name $name).WaitForStatus('Stopped', [TimeSpan]::FromMinutes(2))
    }

    foreach ($path in $directories) {
        $backup = $path + '.WDigger-' + [guid]::NewGuid().ToString('N')
        Move-Item -LiteralPath $path -Destination $backup -ErrorAction Stop
        $renamed += @{ Original = $path; Backup = $backup }
        Write-Output "Copia conservada: $backup"
    }
}
catch {
    $failures.Add($_.Exception.Message)
    [array]::Reverse($renamed)
    foreach ($entry in $renamed) {
        try {
            if (Test-Path -LiteralPath $entry.Original) {
                throw "El destino ya existe: $($entry.Original)"
            }
            Move-Item -LiteralPath $entry.Backup -Destination $entry.Original -ErrorAction Stop
        }
        catch {
            $failures.Add("No se pudo recuperar $($entry.Original): $($_.Exception.Message)")
        }
    }
}
finally {
    foreach ($name in @('cryptsvc', 'bits', 'wuauserv')) {
        if ($running -contains $name) {
            try {
                $service = Get-Service -Name $name -ErrorAction Stop
                if ($service.Status -ne 'Running') {
                    Start-Service -Name $name -ErrorAction Stop
                }
                (Get-Service -Name $name).WaitForStatus('Running', [TimeSpan]::FromMinutes(2))
            }
            catch {
                $failures.Add("No se pudo reiniciar ${name}: $($_.Exception.Message)")
            }
        }
    }
}

if ($failures.Count -gt 0) {
    throw "Restablecimiento incompleto: $($failures -join '; '). Revisa servicios y carpetas WDigger."
}

Write-Output 'Servicios originalmente activos restaurados; las cachés anteriores se conservaron.'
