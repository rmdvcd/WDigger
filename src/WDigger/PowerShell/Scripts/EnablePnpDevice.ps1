param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $InstanceId
)

Enable-PnpDevice -InstanceId $InstanceId -Confirm:$false -ErrorAction Stop
