# Local MCP server (F17)

`ArchitectureGuardian.Mcp` uses the official
[ModelContextProtocol C# SDK 2.2.0](https://www.nuget.org/packages/ModelContextProtocol/2.2.0)
and follows its [stdio getting-started pattern](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/getting-started.html):
standard .NET hosting, stderr console logging, `WithStdioServerTransport()` and
`WithToolsFromAssembly()`.

F17 provides the host. Architecture tools belong to
[F18](https://github.com/kbourzayq/arch-guard/issues/18), with analysis tools in later
issues. There are currently no production tools, prompts or resources. The SDK
therefore advertises no tools capability; `tools/list` and `tools/call` return
method-not-found errors. HTTP hosting, source/policy/baseline mutation, shell
execution and automatic restore are outside this host's capabilities.

## Start and connect

Build outside the protocol session:

```sh
dotnet restore ArchitectureGuardian.slnx --locked-mode
dotnet build ArchitectureGuardian.slnx --configuration Release --no-restore
```

Configure an MCP client to launch the built assembly:

```json
{
  "mcpServers": {
    "architecture-guardian": {
      "command": "dotnet",
      "args": [
        "/absolute/checkout/src/ArchitectureGuardian.Mcp/bin/Release/net10.0/ArchitectureGuardian.Mcp.dll",
        "--Workspace:Root",
        "/absolute/trusted/repository"
      ]
    }
  }
}
```

The standard .NET host reads command-line configuration, environment variables
and configuration files. `Workspace:Root` must name an existing absolute directory;
the options service validates it at startup. For environment configuration use
`Workspace__Root`. Invalid settings fail startup with a nonzero exit code and stderr
diagnostics. The SDK owns stdin and stdout; all console logging goes to stderr.
Use the built assembly in client configuration because `dotnet run` can add build
output to stdout.

The SDK negotiates protocol versions, including the `2025-11-25` initialization
handshake. Closing stdin ends the session. Standard .NET host shutdown and Ctrl+C
stop the server through its normal cancellation lifecycle.

## Tool composition

`AddArchitectureGuardianMcp` registers `IOptions<WorkspaceOptions>` and the existing
`IPolicyLoader` / `YamlPolicyLoader` with dependency injection. Configuration is its only direct
production project dependency. It does not construct analysis rules, load a
workspace, or introduce a session/executor abstraction before tools require them.

Define future tools with `[McpServerToolType]` and `[McpServerTool]`, with descriptions
and read-only annotations. Discovery is restricted to the MCP executable assembly.
A tool receives only the application dependencies it needs through its constructor
or method parameters, calls those services and passes through the SDK-supplied
`CancellationToken`. The SDK supplies discovery, argument binding, JSON schemas,
invocation and result/error serialization. See the official [tools guide](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/tools/tools.html).

## Workspace, cancellation, limits and errors

The operator selects one existing absolute workspace directory. Tools receive
`WorkspaceOptions` through `IOptions<WorkspaceOptions>`. Build trust defaults to false;
set `--Workspace:Trusted true` or `Workspace__Trusted=true` to authorize future
workspace execution. Client roots cannot change these settings, and the host does
not request client roots. F17 has no production file-reading or workspace-loading tool.

Future workspace tools must validate their actual file inputs against the selected
root and retain the existing [workspace trust contract](workspace-loading.md) before
invoking shared services. Those application checks belong with the operations that
need them. The host does not claim filesystem confinement for tools that do not yet
exist, and registering a root does not sandbox trusted MSBuild execution.

| Boundary | Current contract |
| --- | --- |
| Initialization | SDK initialization timeout: 10 seconds |
| Transport input | Standard SDK stdio framing; no additional host byte limit |
| Application concurrency | SDK request dispatch; no additional host concurrency limit |
| Request deadline | No additional host deadline; clients control cancellation |
| Cancellation | SDK request tokens are passed to tool dependencies; cancellation is cooperative |
| Application input/output | No production tools yet; each future tool defines its own relevant input limits, filtering and response bounds |

There is no custom stream wrapper, request executor, busy/timeout error vocabulary
or transport implementation. Limits are intentionally explicit about what this
local host enforces, rather than promising arbitrary global quotas.

The SDK returns JSON-RPC `-32601` for unsupported methods and handles malformed
requests and binding errors. For attributed tools, it converts ordinary application
exceptions into `isError: true` results with generic messages. A deliberate
`McpException` can carry an actionable message; `McpProtocolException` produces a
protocol error, and requested `OperationCanceledException` retains cancellation
semantics. Future analysis tools must preserve incomplete/failed coverage rather
than reporting a clean scan.

## Verification

`Mcp/StdioServerTests` exercises the executable's initialization, capability set,
unsupported-method errors, ping, EOF shutdown and stderr-only output.
`Mcp/WorkspaceOptionsTests` covers standard configuration binding and startup validation.
`Mcp/ToolRegistrationTests` adds an attributed test-only tool to the host's standard
SDK registration. It verifies constructor/method injection, generated schema,
invocation of the shared YAML loader, application error handling and cancellation
reaching a dependency after an explicit protocol cancellation notification. That
test tool is never discovered by the production host.
Dependency tests enforce the Configuration-only project reference in Debug and
Release. These tests run with the normal solution checks.
