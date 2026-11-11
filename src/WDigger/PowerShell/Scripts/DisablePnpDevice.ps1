param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $InstanceId
)

Disable-PnpDevice -InstanceId $InstanceId -Confirm:$false -ErrorAction Stop
