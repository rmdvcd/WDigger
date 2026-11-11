param()

ConvertTo-Json -InputObject @(
    Get-AppxPackage |
        Where-Object { -not $_.IsFramework -and -not $_.IsResourcePackage -and -not $_.NonRemovable } |
        Select-Object Name, PackageFullName
) -Compress
