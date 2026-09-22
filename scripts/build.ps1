param(
    [string]$Configuration = 'Release',
    [string]$TiaPortalV21Dir = 'D:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48',
    [string]$DotnetExe = (Join-Path (Split-Path -Parent $PSScriptRoot) '.tools\dotnet\dotnet.exe')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path $DotnetExe)) { $DotnetExe = 'dotnet' }
Push-Location $root
try {
    & $DotnetExe build .\TiaAgentBridge.sln -c $Configuration -m:1 /p:TiaPortalV21Dir="$TiaPortalV21Dir"
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }

    $hostExe = Join-Path $root "src\TiaAgent.Host\bin\$Configuration\net10.0\TiaAgent.Host.exe"
    if (-not (Test-Path $hostExe)) { throw "Host output not found: $hostExe" }
    Write-Host "Build OK: $hostExe"
    & $hostExe doctor
}
finally {
    Pop-Location
}
