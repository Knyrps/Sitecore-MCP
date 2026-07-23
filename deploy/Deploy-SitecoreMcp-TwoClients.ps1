<#
.SYNOPSIS
  Deploys the SitecoreMCP endpoint with TWO clients: an admin client and a non-admin client. Run ELEVATED.

.DESCRIPTION
  Exactly like Deploy-SitecoreMcp.ps1, but the local dev patch registers a second client mapped to a
  non-admin user (default sitecore\mcp-editor) so you can test limited-user behaviour (ACLs, item
  locking). Generates and prints two keys, and sets both as app-pool-scoped environment variables.

  Create the non-admin Sitecore user yourself first (default: sitecore\mcp-editor) with normal
  content-authoring roles. LOCAL instance only.

.PARAMETER WebRoot
  The Sitecore web root, e.g. C:\inetpub\wwwroot\sitecore.local

.PARAMETER Configuration
  Build configuration to deploy from (default Release).

.PARAMETER Key
  The admin client API key. A random one is generated and printed if omitted.

.PARAMETER EditorKey
  The non-admin client API key. A random one is generated and printed if omitted.

.PARAMETER EditorUser
  The non-admin Sitecore user for the second client (default sitecore\mcp-editor). You must create it.

.PARAMETER AppPool
  The IIS app pool name. Auto-detected from the site binding when omitted.

.EXAMPLE
  ./Deploy-SitecoreMcp-TwoClients.ps1 -WebRoot C:\inetpub\wwwroot\sitecore.local
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WebRoot,
    [string] $Configuration = "Release",
    [string] $Key,
    [string] $EditorKey,
    [string] $EditorUser = "sitecore\mcp-editor",
    [string] $AppPool
)

$ErrorActionPreference = "Stop"
Import-Module WebAdministration -ErrorAction Stop

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    throw "This script must be run from an elevated PowerShell (Run as Administrator)."
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

Write-Host "Writing dev patch (two clients: admin + $EditorUser)..." -ForegroundColor Cyan
$devConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<!--
  LOCAL DEVELOPMENT ONLY. Enables the endpoint over HTTP with two clients:
  an admin client, and a non-admin ($EditorUser) client for testing limited-user behaviour.
  Do not deploy to a shared or production instance.
-->
<configuration xmlns:patch="http://www.sitecore.net/xmlconfig/">
  <sitecore>
    <settings>
      <setting name="Mcp.Enabled">
        <patch:attribute name="value">true</patch:attribute>
      </setting>
      <setting name="Mcp.RequireHttps">
        <patch:attribute name="value">false</patch:attribute>
      </setting>
      <setting name="Mcp.AllowWrites">
        <patch:attribute name="value">true</patch:attribute>
      </setting>
      <setting name="Mcp.VerboseErrors">
        <patch:attribute name="value">true</patch:attribute>
      </setting>
    </settings>
    <sitecoreMcp>
      <clients>
        <client id="admin" keyEnvVar="SITECORE_MCP_KEY" user="sitecore\admin" allowWrites="true" databases="master|web|core" />
        <client id="editor" keyEnvVar="SITECORE_MCP_KEY_EDITOR" user="$EditorUser" allowWrites="true" databases="master" />
      </clients>
    </sitecoreMcp>
  </sitecore>
</configuration>
"@
Set-Content -Path (Join-Path $includeTarget "SitecoreMcp.Dev.config") -Value $devConfig -Encoding UTF8

if ([string]::IsNullOrWhiteSpace($Key)) {
    $Key = [Guid]::NewGuid().ToString("N") + [Guid]::NewGuid().ToString("N")
    Write-Host "Generated admin API key:  $Key" -ForegroundColor Yellow
}
if ([string]::IsNullOrWhiteSpace($EditorKey)) {
    $EditorKey = [Guid]::NewGuid().ToString("N") + [Guid]::NewGuid().ToString("N")
    Write-Host "Generated editor API key: $EditorKey" -ForegroundColor Yellow
}

if ([string]::IsNullOrWhiteSpace($AppPool)) {
    $site = Get-Website | Where-Object { $_.PhysicalPath -and ($_.PhysicalPath.TrimEnd('\') -ieq $WebRoot.TrimEnd('\')) } | Select-Object -First 1
    if (-not $site) { throw "Could not auto-detect the site for '$WebRoot'. Pass -AppPool explicitly." }
    $AppPool = $site.applicationPool
}
Write-Host "App pool: $AppPool" -ForegroundColor Cyan

# Scope the keys to the app pool's environment rather than machine-wide variables. Update the
# existing entry in place (or add it), via the WebAdministration cmdlets which reliably write
# applicationHost.config where appcmd's nested-collection syntax silently no-ops.
$appHost = "MACHINE/WEBROOT/APPHOST"
function Set-AppPoolEnv([string] $pool, [string] $name, [string] $value) {
    $col = "system.applicationHost/applicationPools/add[@name='$pool']/environmentVariables"
    $existing = Get-WebConfiguration -pspath $appHost -filter "$col/add[@name='$name']" -ErrorAction SilentlyContinue
    if ($existing) {
        Set-WebConfigurationProperty -pspath $appHost -filter "$col/add[@name='$name']" -name "value" -value $value
    }
    else {
        Add-WebConfigurationProperty -pspath $appHost -filter $col -name "." -value @{ name = $name; value = $value }
    }
}
Set-AppPoolEnv $AppPool "SITECORE_MCP_KEY" $Key
Set-AppPoolEnv $AppPool "SITECORE_MCP_KEY_EDITOR" $EditorKey

Write-Host "App pool environment variables now set to:" -ForegroundColor Cyan
Get-WebConfiguration -pspath $appHost -filter "system.applicationHost/applicationPools/add[@name='$AppPool']/environmentVariables/add" |
    ForEach-Object { "  $($_.name) = $($_.value)" }

Write-Host ""
Write-Host "Deployed two clients. Create the non-admin user '$EditorUser' if you have not already." -ForegroundColor Green
Write-Host "Verify each key:" -ForegroundColor Green
Write-Host "  ./deploy/Verify-SitecoreMcp.ps1 -Url https://<host>/sitecore/api/mcp -Key $Key"
Write-Host "  ./deploy/Verify-SitecoreMcp.ps1 -Url https://<host>/sitecore/api/mcp -Key $EditorKey"
