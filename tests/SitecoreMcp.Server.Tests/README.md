# SitecoreMcp.Server.Tests

Unit tests for the parts of the server that do not need a running Sitecore instance: the
protocol layer, the schema generator, and the request guards.

```powershell
dotnet test
```

## Scope

Covered here:

- Dispatcher: every method, malformed JSON, unknown method, bad params.
- Notifications: every path returns 202 with no body, including unknown notifications.
- Guards: `Origin`, `MCP-Protocol-Version`, oversized bodies, `Content-Type`, `Accept`.
- Schema generator output.
- Fixed-time key comparison.
- Rate limiter: bucket exhaustion and refill.

Not covered here — these need a real instance and are run manually against it (see the root
README and `docs/`): item and field ACLs, `CancelEdit` on failed writes, database allowlisting,
concurrency limits, app-pool recycling, and client compatibility.

## Keeping these tests instance-free

This only works while `Protocol/` and `Schema/` have no Sitecore references and guard logic
stays pure. If a test suddenly needs `Sitecore.Kernel` at runtime, that is a signal the
production code has leaked a dependency in the wrong direction — fix the dependency rather than
the test.
