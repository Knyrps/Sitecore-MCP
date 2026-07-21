# Deploy

Deploying into an IIS-hosted Sitecore instance needs an **elevated** shell: the web root, the
app-pool environment, and app-pool recycling are all admin-only.

## Steps

1. Build against the target instance's assemblies. Point the build at it with a gitignored
   `Directory.Build.user.props` at the repo root:

   ```xml
   <Project>
     <PropertyGroup>
       <SitecoreWebRoot>C:\inetpub\wwwroot\local.knh.de</SitecoreWebRoot>
     </PropertyGroup>
   </Project>
   ```

   ```powershell
   dotnet build -c Release
   ```

   The server DLL binds to the exact `Sitecore.Kernel` and `Newtonsoft.Json` in that web root, so
   it must be built against the instance it will run in (10.3 and 10.4 ship different Kernel
   assembly versions).

2. From an **elevated** PowerShell, deploy and enable it locally:

   ```powershell
   ./deploy/Deploy-SitecoreMcp.ps1 -WebRoot C:\inetpub\wwwroot\local.knh.de
   ```

   This copies the DLL and `SitecoreMcp.config`, writes a `SitecoreMcp.Dev.config` that enables the
   endpoint over HTTP with an admin-mapped client, sets `SITECORE_MCP_KEY` on the app pool, and
   recycles it. It prints the generated key.

3. Verify:

   ```powershell
   ./deploy/Verify-SitecoreMcp.ps1 -Url http://local.knh.de/sitecore/api/mcp -Key <printed-key>
   ```

   Expect an `initialize` result, four tools listed, and a `sitecore_get_context` payload with the
   instance name, version, and resolved user. Then check that `App_Data/logs/mcp.log.<date>.txt`
   exists and contains an `AUDIT` line.

## Production

The dev patch is local-only: it disables HTTPS and maps an admin user. On a shared instance, skip
it — keep `Mcp.Enabled=false` in the base config until you add an environment patch that leaves
HTTPS on, maps a dedicated limited Sitecore user, and grants writes only if required.
