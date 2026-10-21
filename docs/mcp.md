# Local MCP server (F17)

`ArchitectureGuardian.Mcp` is a read-only, stdio-only host using the official
[ModelContextProtocol C# SDK 2.2.0](https://www.nuget.org/packages/ModelContextProtocol/2.2.0).
It supports the SDK's protocol negotiation, including initialization with
`2025-11-25`. No HTTP listener or remote hosting is provided.

F17 establishes transport and application composition. `tools/list` returns an
empty `tools` array. Architecture context and configured rule tools belong to
[F18](https://github.com/kbourzayq/arch-guard/issues/18); analysis tools follow in
later issues. There are no source, policy or baseline mutation tools, resources,
prompts, sampling, shell execution or automatic restore operations.

## Start and connect

Build once outside the protocol session:

```sh
dotnet restore ArchitectureGuardian.slnx --locked-mode
dotnet build ArchitectureGuardian.slnx --configuration Release --no-restore
```

Configure an MCP client to launch the built assembly with an explicit workspace:

```json
{
  "mcpServers": {
    "architecture-guardian": {
      "command": "dotnet",
      "args": [
        "/absolute/checkout/src/ArchitectureGuardian.Mcp/bin/Release/net10.0/ArchitectureGuardian.Mcp.dll",
        "--workspace-root",
        "/absolute/trusted/repository"
      ]
    }
  }
}
```

Use absolute paths in client configuration. The root must already exist. Unknown,
missing or duplicated options fail startup with exit code 2 and a stderr diagnostic.
The server accepts newline-delimited JSON-RPC on stdin; stdout contains protocol
messages only. All host/SDK console logging goes to stderr, including normal
startup and shutdown messages. Avoid `dotnet run` in client configuration because
build output can contaminate the protocol stream. Closing stdin ends the session;
Ctrl+C and host shutdown cancel outstanding SDK requests.

## Workspace and trust contract

The operator selects exactly one root using `--workspace-root`. Relative file
inputs in future tools resolve against that directory, never the process working
directory. `WorkspaceSession` routes policy loading through `IPolicyLoader` /
`YamlPolicyLoader` and prepares `WorkspaceLoadRequest` for Analysis; the host also
registers `IAnalysisService<DependencyBuildResult>` with the same AG001–AG006 rule
implementations as CLI. No rule semantics or console rendering are duplicated.

Existing file inputs are resolved physically, including intermediate symbolic links
and junctions, including links inside other link targets, and must stay inside the
physical root. Link cycles or more than 64 nested link expansions are rejected. Traversal, sibling-prefix
paths, missing files and symbolic links to files outside the root are rejected.
Client-advertised roots and roots-change notifications cannot grant access or alter
this setting. The server does not request client roots.

Workspace execution defaults to untrusted. Only the operator's startup flag
`--trust-workspace` authorizes future workspace loading to execute repository
MSBuild logic; protocol parameters cannot grant trust. F17 performs no workspace
load at initialization or tool listing. See [workspace loading](workspace-loading.md)
for the design-time build contract. Root checks constrain explicit input paths;
they are not an OS sandbox for MSBuild imports, project references, generators or
concurrent filesystem changes. Trusted repositories can execute build logic and
write intermediate outputs. Run with OS isolation when needed. The adapter itself
provides no write use case for source, policy or baselines.

## Limits, cancellation and errors

| Boundary | Contract |
| --- | --- |
| Input frame | At most 1,048,576 bytes before each newline, across reads; larger frames close the session and report on stderr before parsing the full frame |
| Policy input | At most 1,048,576 bytes at validation time before invoking the shared loader |
| Initialization | SDK initialization timeout: 10 seconds |
| Application requests | One active request, no waiting queue; a concurrent request fails with `request_busy` |
| Application deadline | 30 seconds, linked to SDK request cancellation and host shutdown; cooperative cancellation |
| Output | F17 returns fixed, bounded metadata and an empty catalog; later tools must define response limits and filtering |

The executor passes the linked token to shared services. A client cancellation
remains `OperationCanceledException` for the SDK to handle; deadline expiry becomes
`request_timeout`. Capacity is held until cooperative work actually ends, including
work that ignores cancellation, so cancelling cannot start overlapping workspace
loads. A result arriving after cancellation is discarded. This is not a hard
process-kill deadline. Protocol control and cancellation messages bypass the
application concurrency slot.

The SDK owns JSON-RPC parsing, negotiation, parameter binding and request-ID matching.
Unknown methods return `-32601` (method not found); unknown tools return `-32602`
(invalid params). Application failures in future registered tools use MCP
`isError: true` results: `request_busy`, `request_timeout`, invalid input/policy and
incomplete/failed analysis must never be represented as clean analysis. Invalid
paths and policies preserve the shared validation exception contracts internally;
tool adapters must translate these into actionable messages without stack traces.
Operational coverage remains distinct from architecture findings and gate outcomes.
No F17 tool can currently produce these future application results.

## Verification

`Mcp/StdioServerTests` exercises the real executable: initialization, capabilities,
empty tools, unknown tool/method errors, cancellation-notification handling, ping,
EOF shutdown, oversized frames and stderr-only logs. `Mcp/RequestBoundaryTests`
covers startup validation, physical path confinement, unchanged policy contents,
shared YAML loading, build trust, input limits, deadline expiry, client cancellation,
concurrent rejection and capacity release after failure. These run with the normal
solution checks; process tests carry the `Integration` trait. Dependency boundary
tests check the MCP adapter in Debug and Release.
