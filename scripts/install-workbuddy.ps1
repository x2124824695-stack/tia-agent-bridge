param(
    [ValidateSet('inspect','engineering','online','control')]
    [string]$Access = 'engineering',

    [string]$Name = 'tia-agent',

    [string]$McpConfig = "$env:USERPROFILE\.workbuddy\mcp.json"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$hostExe = Join-Path $root 'src\TiaAgent.Host\bin\Release\net10.0\TiaAgent.Host.exe'
if (-not (Test-Path $hostExe)) { throw "Host not built yet: $hostExe. Run scripts\build.ps1 first." }

$dotnetRoot = Join-Path $root '.tools\dotnet'
$api = 'D:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48'
if (-not (Test-Path $api)) { $api = 'C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48' }

$entry = [ordered]@{
    command = $hostExe.Replace('\', '/')
    args    = @('--access', $Access)
    env     = [ordered]@{
        DOTNET_ROOT         = $dotnetRoot.Replace('\', '/')
        DOTNET_ROOT_X64     = $dotnetRoot.Replace('\', '/')
        TIA_PORTAL_PUBLIC_API = $api.Replace('\', '/')
    }
}

if (Test-Path $McpConfig) {
    $json = Get-Content -LiteralPath $McpConfig -Raw -Encoding UTF8 | ConvertFrom-Json
} else {
    $json = New-Object psobject
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $McpConfig) | Out-Null
}

if (-not $json.PSObject.Properties['mcpServers']) {
    $json | Add-Member -NotePropertyName mcpServers -NotePropertyValue (New-Object psobject)
}
$json.mcpServers | Add-Member -NotePropertyName $Name -NotePropertyValue ([pscustomobject]$entry) -Force

$out = $json | ConvertTo-Json -Depth 12
[System.IO.File]::WriteAllText($McpConfig, $out, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Registered MCP server '$Name' (access=$Access) in $McpConfig"
Write-Host ''
Write-Host 'The new server does not activate automatically. Open WorkBuddy > Connectors,'
Write-Host "find '$Name' under custom connectors and click Trust to enable it."
