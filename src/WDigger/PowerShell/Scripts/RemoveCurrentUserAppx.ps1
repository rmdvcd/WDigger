param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9._-]{1,240}$')]
    [string] $PackageFullName
)

$package = @(Get-AppxPackage |
    Where-Object {
        $_.PackageFullName -ceq $PackageFullName -and
        -not $_.IsFramework -and
        -not $_.IsResourcePackage -and
        -not $_.NonRemovable
    })

if ($package.Count -ne 1) {
    throw 'El paquete ya no está disponible o no es apto para retirarse.'
}

Remove-AppxPackage -Package $package[0].PackageFullName -ErrorAction Stop
Write-Output 'El paquete se retiró para el usuario de la sesión actual.'
