param([string]$DotnetExe = (Join-Path (Split-Path -Parent $PSScriptRoot) '.tools/dotnet/dotnet.exe'))
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $DotnetExe)) { $DotnetExe = 'dotnet' }
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    & $DotnetExe build TiaAgentBridge.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed. Run build.ps1 / dotnet restore first.' }
    & $DotnetExe run --project tests/TiaAgent.Tests/TiaAgent.Tests.csproj -c Release -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    & $DotnetExe build tests/FakeWorker/FakeWorker.csproj -c Release -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Fault injection worker build failed.' }
    python tests/test_stdio.py
    if ($LASTEXITCODE -ne 0) { throw 'Protocol tests failed.' }
} finally { Pop-Location }
