<#
.SYNOPSIS
  Deploys the SitecoreMCP endpoint into an IIS-hosted Sitecore instance. Run ELEVATED, or pass
  -SkipAdminRequirement on a machine where this account already holds the needed rights.

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

.PARAMETER SkipAdminRequirement
  Skips the elevation check, for a machine where the account has been granted the needed rights
  directly (e.g. write access to the web root). This only skips the check - it grants nothing, so
  steps that genuinely require administrator rights still fail without them.

.EXAMPLE
  ./Deploy-SitecoreMcp.ps1 -WebRoot C:\inetpub\wwwroot\sitecore.local
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WebRoot,
    [string] $Configuration = "Release",
    [string] $Key,
    [string] $AppPool,
    [switch] $SkipAdminRequirement
)

$ErrorActionPreference = "Stop"
Import-Module WebAdministration -ErrorAction Stop

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $isAdmin) {
    if (-not $SkipAdminRequirement) {
        throw "This script must be run from an elevated PowerShell (Run as Administrator). Pass -SkipAdminRequirement if this account has already been granted the rights it needs."
    }

    # The switch waives the check, not the permissions: writing app-pool environment variables edits
    # applicationHost.config, and starting/stopping the pool talks to the IIS service - both still
    # need administrator rights and will fail here without them.
    Write-Warning "Running without elevation (-SkipAdminRequirement). Copying files works if this account can write to the web root, but setting app-pool environment variables and restarting the pool still require administrator rights."
}

$repoRoot = Split-Path $PSScriptRoot -Parent
$serverDir = Join-Path $repoRoot "src\SitecoreMcp.Server"

# Build here rather than trusting whatever is already in bin\$Configuration. Deploying a pre-built
# artifact silently ships stale code when it was built for a different configuration or not rebuilt
# after an edit (a hash check on the copy cannot catch that - the stale source copies cleanly).
Write-Host "Building $Configuration against $WebRoot..." -ForegroundColor Cyan
& dotnet build (Join-Path $serverDir "SitecoreMcp.Server.csproj") -c $Configuration -p:SitecoreWebRoot=$WebRoot --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed; aborting deploy so a stale assembly is never shipped." }

$dll = Join-Path $serverDir "bin\$Configuration\SitecoreMcp.Server.dll"
if (-not (Test-Path $dll)) {
    throw "Build output not found at $dll after build."
}

$binTarget = Join-Path $WebRoot "bin"
$includeTarget = Join-Path $WebRoot "App_Config\Include\SitecoreMcp"
if (-not (Test-Path $binTarget)) { throw "Web root bin not found: $binTarget" }
New-Item -ItemType Directory -Force -Path $includeTarget | Out-Null

Write-Host "Copying assembly..." -ForegroundColor Cyan
Copy-Item $dll $binTarget -Force
$pdb = [IO.Path]::ChangeExtension($dll, ".pdb")
if (Test-Path $pdb) { Copy-Item $pdb $binTarget -Force }

# Verify the copy actually landed. A silently failed or skipped copy (e.g. a locked file, or the
# script aborting before this point on a re-run) otherwise leaves the old assembly serving requests.
$deployedDll = Join-Path $binTarget (Split-Path $dll -Leaf)
$srcHash = (Get-FileHash $dll).Hash
$dstHash = (Get-FileHash $deployedDll -ErrorAction SilentlyContinue).Hash
if ($srcHash -ne $dstHash) {
    throw "Deployed assembly hash does not match the build. source=$srcHash deployed=$dstHash. The copy did not land."
}
Write-Host "Assembly verified: $dstHash" -ForegroundColor Green

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

Write-Host "Restarting app pool (full stop then start, so the worker re-reads its environment)..." -ForegroundColor Cyan
try { if ((Get-WebAppPoolState -Name $AppPool).Value -ne "Stopped") { Stop-WebAppPool -Name $AppPool } } catch { }
$tries = 0
while ((Get-WebAppPoolState -Name $AppPool).Value -ne "Stopped" -and $tries -lt 40) { Start-Sleep -Milliseconds 250; $tries++ }
Start-WebAppPool -Name $AppPool

Write-Host ""
Write-Host "Deployed. Verify with:" -ForegroundColor Green
Write-Host "  ./deploy/Verify-SitecoreMcp.ps1 -Url http[s]://<host>/sitecore/api/mcp -Key $Key"
