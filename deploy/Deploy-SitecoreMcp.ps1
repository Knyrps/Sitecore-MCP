<#
.SYNOPSIS
  Deploys the SitecoreMCP endpoint into an IIS-hosted Sitecore instance. Run ELEVATED.

.DESCRIPTION
  Copies the built server assembly and config into the web root, writes a local dev patch that
  enables the endpoint, sets the API key as an app-pool-scoped environment variable, and recycles
  the app pool so the initialize pipeline registers the route.

  The dev patch enables the endpoint over HTTP with an admin-mapped client. It is intended for a
  LOCAL instance only. Do not run this against a shared or production instance.

.PARAMETER WebRoot
  The Sitecore web root, e.g. C:\inetpub\wwwroot\sitecore.local

.PARAMETER Configuration
  Build configuration to deploy from (default Release).

.PARAMETER Key
  The API key. A random one is generated and printed if omitted.

.PARAMETER AppPool
  The IIS app pool name. Auto-detected from the site binding when omitted.

.EXAMPLE
  ./Deploy-SitecoreMcp.ps1 -WebRoot C:\inetpub\wwwroot\sitecore.local
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WebRoot,
    [string] $Configuration = "Release",
    [string] $Key,
    [string] $AppPool
)

$ErrorActionPreference = "Stop"
Import-Module WebAdministration -ErrorAction Stop

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    throw "This script must be run from an elevated PowerShell (Run as Administrator)."
}

$repoRoot = Split-Path $PSScriptRoot -Parent
$serverDir = Join-Path $repoRoot "src\SitecoreMcp.Server"
$dll = Join-Path $serverDir "bin\$Configuration\SitecoreMcp.Server.dll"
if (-not (Test-Path $dll)) {
    throw "Build output not found at $dll. Build first: dotnet build -c $Configuration"
}

$binTarget = Join-Path $WebRoot "bin"
$includeTarget = Join-Path $WebRoot "App_Config\Include\SitecoreMcp"
if (-not (Test-Path $binTarget)) { throw "Web root bin not found: $binTarget" }
New-Item -ItemType Directory -Force -Path $includeTarget | Out-Null

Write-Host "Copying assembly..." -ForegroundColor Cyan
Copy-Item $dll $binTarget -Force
$pdb = [IO.Path]::ChangeExtension($dll, ".pdb")
if (Test-Path $pdb) { Copy-Item $pdb $binTarget -Force }

Write-Host "Copying base config..." -ForegroundColor Cyan
Copy-Item (Join-Path $serverDir "App_Config\Include\SitecoreMcp\SitecoreMcp.config") $includeTarget -Force

Write-Host "Writing dev patch (enables endpoint locally)..." -ForegroundColor Cyan
Copy-Item (Join-Path $serverDir "App_Config\Include\SitecoreMcp\SitecoreMcp.Dev.config.example") `
          (Join-Path $includeTarget "SitecoreMcp.Dev.config") -Force

if ([string]::IsNullOrWhiteSpace($Key)) {
    $Key = [Guid]::NewGuid().ToString("N") + [Guid]::NewGuid().ToString("N")
    Write-Host "Generated API key: $Key" -ForegroundColor Yellow
}

if ([string]::IsNullOrWhiteSpace($AppPool)) {
    $site = Get-Website | Where-Object { $_.PhysicalPath -and ($_.PhysicalPath.TrimEnd('\') -ieq $WebRoot.TrimEnd('\')) } | Select-Object -First 1
    if (-not $site) { throw "Could not auto-detect the site for '$WebRoot'. Pass -AppPool explicitly." }
    $AppPool = $site.applicationPool
}
Write-Host "App pool: $AppPool" -ForegroundColor Cyan

# Scope the key to the app pool's environment rather than a machine-wide variable. Update in place
# (or add) via the WebAdministration cmdlets, which reliably write applicationHost.config where
# appcmd's nested-collection syntax silently no-ops.
$appHost = "MACHINE/WEBROOT/APPHOST"
$envCol = "system.applicationHost/applicationPools/add[@name='$AppPool']/environmentVariables"
$existing = Get-WebConfiguration -pspath $appHost -filter "$envCol/add[@name='SITECORE_MCP_KEY']" -ErrorAction SilentlyContinue
if ($existing) {
    Set-WebConfigurationProperty -pspath $appHost -filter "$envCol/add[@name='SITECORE_MCP_KEY']" -name "value" -value $Key
}
else {
    Add-WebConfigurationProperty -pspath $appHost -filter $envCol -name "." -value @{ name = "SITECORE_MCP_KEY"; value = $Key }
}
Write-Host "SITECORE_MCP_KEY on '$AppPool' set to: $((Get-WebConfiguration -pspath $appHost -filter "$envCol/add[@name='SITECORE_MCP_KEY']").value)" -ForegroundColor Cyan

Write-Host "Recycling app pool..." -ForegroundColor Cyan
Restart-WebAppPool -Name $AppPool

Write-Host ""
Write-Host "Deployed. Verify with:" -ForegroundColor Green
Write-Host "  ./deploy/Verify-SitecoreMcp.ps1 -Url http[s]://<host>/sitecore/api/mcp -Key $Key"
