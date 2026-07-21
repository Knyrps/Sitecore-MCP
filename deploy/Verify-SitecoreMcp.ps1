<#
.SYNOPSIS
  Smoke-tests a deployed SitecoreMCP endpoint: initialize, tools/list, and get_context.

.PARAMETER Url
  The endpoint URL, e.g. https://sitecore.local/sitecore/api/mcp

.PARAMETER Key
  The API key configured for a client.

.EXAMPLE
  ./Verify-SitecoreMcp.ps1 -Url https://sitecore.local/sitecore/api/mcp -Key <key>
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Url,
    [Parameter(Mandatory = $true)] [string] $Key
)

$ErrorActionPreference = "Stop"
$version = "2025-06-18"

function Invoke-Mcp {
    param([string] $Body, [switch] $WithVersion)
    $headers = @{ Authorization = "Bearer $Key"; Accept = "application/json" }
    if ($WithVersion) { $headers["MCP-Protocol-Version"] = $version }
    return Invoke-RestMethod -Method Post -Uri $Url -Headers $headers -ContentType "application/json" -Body $Body
}

Write-Host "1. initialize" -ForegroundColor Cyan
$init = Invoke-Mcp '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}'
$version = $init.result.protocolVersion
Write-Host "   protocolVersion=$version server=$($init.result.serverInfo.name) $($init.result.serverInfo.version)"

Write-Host "2. tools/list" -ForegroundColor Cyan
$tools = Invoke-Mcp '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' -WithVersion
$tools.result.tools | ForEach-Object { Write-Host "   - $($_.name)" }

Write-Host "3. tools/call sitecore_get_context" -ForegroundColor Cyan
$ctx = Invoke-Mcp '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"sitecore_get_context","arguments":{}}}' -WithVersion
$ctx.result.structuredContent | ConvertTo-Json -Depth 5

Write-Host ""
Write-Host "OK" -ForegroundColor Green
