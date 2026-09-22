# Adds the current Windows user to the local group required by TIA Portal Openness.
# Must be run from an ELEVATED (Run as administrator) PowerShell or Terminal.
$ErrorActionPreference = 'Stop'

$group = 'Siemens TIA Openness'
$user  = "$env:USERDOMAIN\$env:USERNAME"

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'ERROR: this script must be run as Administrator.' -ForegroundColor Red
    exit 1
}

$exists = Get-LocalGroup -Name $group -ErrorAction SilentlyContinue
if (-not $exists) {
    Write-Host "ERROR: local group '$group' was not found. Is TIA Portal V21 Openness installed?" -ForegroundColor Red
    exit 1
}

Add-LocalGroupMember -Group $group -Member $user -ErrorAction SilentlyContinue
if ($?) { Write-Host "Added $user to '$group'." }

Write-Host ''
Write-Host "Members of '$group' now:"
Get-LocalGroupMember -Group $group | ForEach-Object { Write-Host ("  - " + $_.Name) }

Write-Host ''
Write-Host 'Sign out of Windows and sign back in (or reboot) so the new group token takes effect,' -ForegroundColor Yellow
Write-Host 'then start TIA Portal V21 before using the TiaAgentBridge MCP.' -ForegroundColor Yellow
