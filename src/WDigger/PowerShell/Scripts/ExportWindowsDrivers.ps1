param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $Destination
)

Export-WindowsDriver -Online -Destination $Destination -Verbose -ErrorAction Stop
