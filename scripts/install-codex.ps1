param(
    [Parameter(Mandatory=$true)]
    [string]$HostPath,

    [ValidateSet('inspect','engineering','online','control')]
    [string]$Access = 'inspect',

    [string]$Name = 'tia-agent'
)

$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path $HostPath).Path

Write-Host "Registering MCP server '$Name' with Codex..."
& codex mcp add $Name -- $resolved --access $Access
if ($LASTEXITCODE -ne 0) {
    throw "codex mcp add failed with exit code $LASTEXITCODE"
}

Write-Host "Registered. Verify with: codex mcp get $Name"
