param([Parameter(Mandatory=$true)][string]$IniPath,[Parameter(Mandatory=$true)][string]$BridgePath,[ValidateSet('Add','Remove')][string]$Mode='Add')
try {
    . (Join-Path $PSScriptRoot 'Install.Common.ps1')
    Set-BridgeRegistration $IniPath $BridgePath ($Mode -eq 'Remove')
    exit 0
} catch {Write-Error $_;exit 1}
