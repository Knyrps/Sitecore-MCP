# Extensions

Optional tool assemblies that plug into the server without the server depending on them. An extension
uses the server's own types (`McpTool<TArgs>`, `McpParam`, `McpToolResult`, `McpCallContext`,
`ConfirmDirectChange`, `McpLog`) and registers its tools through a config patch that adds to the
server's `<tools hint="raw:AddTool">` list.

Extensions are not part of `SitecoreMCP.slnx`. Build and deploy each one only to an instance where it
applies.

## Layout

```
extensions/
  Directory.Build.props     shared: server reference (compile-only), Sitecore assemblies from the instance
  Directory.Build.targets   shared: reference check, opt-in deploy
  <Name>/
    <Name>.csproj           only what the extension adds (e.g. a reference to the module it wraps)
    *.cs                    the tools
    App_Config/Include/SitecoreMcp/<Name>.config   registers the tools
```

## Build and deploy

```powershell
dotnet build extensions\SitecoreMcp.Extensions.Spe -c Release /p:DeployToSitecore=true /p:SitecoreWebRoot=C:\inetpub\wwwroot\<instance>
```

`DeployToSitecore` copies the extension DLL and its config patch only; it never redeploys the server.
Passing `SitecoreWebRoot` explicitly avoids deploying to whatever `Directory.Build.user.props` points at.
Adding the config patch restarts the Sitecore application.

## Available extensions

| Extension | Tool | Needs |
|---|---|---|
| `SitecoreMcp.Extensions.Spe` | `sitecore_run_powershell`: runs a PowerShell script in a throwaway SPE session as the calling user and returns output and errors. Admin and write clients only; a direct change; every script is logged to `mcp.log` with its SHA-256. | Sitecore PowerShell Extensions |
