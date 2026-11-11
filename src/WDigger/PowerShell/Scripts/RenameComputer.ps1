param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9-]{1,15}$')]
    [string] $ComputerName
)

Rename-Computer -NewName $ComputerName -Restart:$false -ErrorAction Stop
Write-Output 'El cambio de nombre está programado y requiere reiniciar Windows.'
