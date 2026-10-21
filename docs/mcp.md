# Local MCP server and architecture/analysis tools (F17–F19)

`ArchitectureGuardian.Mcp` uses the official
[ModelContextProtocol C# SDK 2.2.0](https://www.nuget.org/packages/ModelContextProtocol/2.2.0)
and follows its [stdio getting-started pattern](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/getting-started.html):
standard .NET hosting, stderr console logging, `WithStdioServerTransport()` and
`WithToolsFromAssembly()`.

F17 provides the host; [F18](https://github.com/kbourzayq/arch-guard/issues/18) adds
`get_architecture` and `list_rules`. F19 adds `analyze_solution` and the explicit
unsupported `analyze_changes` interface. The SDK advertises all four read-only tools
with input/output JSON Schemas. No prompts or resources are registered. HTTP hosting, source/policy/baseline mutation, shell
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
`IPolicyLoader` / `YamlPolicyLoader` with dependency injection. The adapter references
Configuration for policy loading and Analysis for trusted
workspace loading and repository normalization. CLI and MCP share the
`SolutionRules.All` inventory, canonical Core catalog, severity policy and strict
project ownership resolution. `analyze_solution` and CLI `scan` call the same
`SolutionAnalysisService`, including rule selection before builds, extraction and
`AnalysisService` evaluation. Core `GatePolicy` evaluates completed results;
architecture/rule metadata tools do not evaluate gates.

Tools use `[McpServerToolType]` and `[McpServerTool]`, with descriptions
and read-only annotations. Discovery is restricted to the MCP executable assembly.
A tool receives only the application dependencies it needs through its constructor
or method parameters, calls those services and passes through the SDK-supplied
`CancellationToken`. The SDK supplies discovery, argument binding, JSON schemas,
invocation and result/error serialization. See the official [tools guide](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/tools/tools.html).

## Workspace, cancellation, limits and errors

The operator selects one existing absolute workspace directory. Tools receive
`WorkspaceOptions` through `IOptions<WorkspaceOptions>`. Build trust defaults to false;
set `--Workspace:Trusted true` or `Workspace__Trusted=true` to authorize
workspace execution. Client roots cannot change these settings, and the host does
not request client roots. `list_rules` reads policy without build trust.
`get_architecture` and `analyze_solution` pass the operator
trust setting to the shared loader; a client cannot enable it per call.

Explicit policy/input paths are resolved relative to the operator root, must name
existing files inside it, and cannot traverse symbolic links or reparse points
below that root. Absolute paths inside the root are accepted. The configured root
itself is operator-owned. These checks are preflight validation: concurrent file
replacement is outside the contract. The root does not sandbox trusted MSBuild,
its imports, referenced projects, generators or SDK discovery. Trust only repositories
whose build logic you authorize. The existing [workspace trust contract](workspace-loading.md),
no-restore behavior and one-SDK-per-process limitation apply.

| Boundary | Current contract |
| --- | --- |
| Initialization | SDK initialization timeout: 10 seconds |
| Transport input | Standard SDK stdio framing; no additional host byte limit |
| Application concurrency | SDK request dispatch; no additional host concurrency limit |
| Request deadline | No additional host deadline; clients control cancellation |
| Cancellation | SDK request tokens are passed to tool dependencies; cancellation is cooperative |
| Application input/output | Explicit paths: 4096 characters; policy file preflight: 262144 bytes; configuration/framework and module/layer filters: 128 characters; base reference: 256 characters; comparison scope: exact `solution`; each successful JSON payload: 65536 UTF-8 bytes |

There is no custom stream wrapper, request executor, busy/timeout error vocabulary
or transport implementation. Limits are intentionally explicit about what this
local host enforces, rather than promising arbitrary global quotas.

The SDK returns JSON-RPC `-32601` for unsupported methods and handles malformed
requests and binding errors. For attributed tools, it converts ordinary application
exceptions into `isError: true` results with generic messages. A deliberate
`McpException` can carry an actionable message; `McpProtocolException` produces a
protocol error, and requested `OperationCanceledException` retains cancellation
semantics. `get_architecture` and `analyze_solution` preserve incomplete/failed coverage and its operational issues.
Requested cancellation is propagated to SDK cancellation even though the shared
workspace services internally represent it as a coverage status. Invalid policies,
unknown filters and oversized output produce tool errors, without partial payloads.
Unexpected exceptions retain the SDK generic error message.

## Verification

`Mcp/StdioServerTests` exercises the executable's initialization, capability set,
unsupported-method errors, ping, EOF shutdown and stderr-only output.
`Mcp/WorkspaceOptionsTests` covers standard configuration binding and startup validation.
`Mcp/ToolRegistrationTests` adds an attributed test-only tool to the host's standard
SDK registration. It verifies constructor/method injection, generated schema,
invocation of the shared YAML loader, application error handling and cancellation
reaching a dependency after an explicit protocol cancellation notification. That
test tool is never discovered by the production host.
`Mcp/ArchitectureToolTests` covers production schemas, policy parity, rule selection
and filtering, real workspace ownership/contracts, restriction data, partial coverage,
invalid mappings hidden behind filters, trust, path/link checks, policy/output bounds
and cancellation before serialization. Dependency tests enforce the Analysis and
Configuration project references in Debug and Release. These tests run with the normal
solution checks.
`Mcp/AnalysisToolTests` checks shared CLI scan parity, compliant/violating and partial
inputs, severity gates, evidence, unsupported comparisons, required base/scope,
trust/input errors, oversized diagnostic output and pre-cancelled calls.
`ToolRegistrationTests` also cancels both analysis tools during policy loading
through an explicit protocol notification.

## Tool requests and response schemas (version 1)

All tools return the same JSON object in `structuredContent` and one text block
for clients without structured-content support. `tools/list` publishes the SDK-generated
`inputSchema` and `outputSchema` from the tool methods and response records in
`src/ArchitectureGuardian.Mcp/Tools`. Schema changes are versioned through `schemaVersion`.
Successful payloads are never silently truncated. The 65536-byte cap applies to each
JSON copy; the JSON-RPC envelope includes both copies and SDK framing overhead.
On a limit error, narrow filters or input/policy scope. There is no pagination.
Collections follow the shared policy/model's ordinal ordering. Filters are exact,
case-sensitive IDs; unknown IDs are errors.

### `list_rules`

Arguments: required `policyPath`; optional `ruleId` (AG001–AG006). Example:

```json
{"policyPath":"architecture.guardian.yaml","ruleId":"AG001"}
```

The response has `schemaVersion: 1`, `status: "completed"`, `scope: "policy"`,
root-relative `policyPath`, `inventoryStatus: "not-loaded"`, and `rules: Rule[]`.
Completion means the policy document passed the same YAML/Core validation as CLI.
Project selectors have **not** been resolved; no workspace coverage or clean scan is
claimed. `rules` includes all six canonical rules by default, including deselected
ones. `ruleId` narrows it to one entry.

| `Rule` field | Type and meaning |
| --- | --- |
| `id` | string; canonical AG001–AG006 ID |
| `description` | string; canonical human-readable check title |
| `documentationUrl` | string; canonical help URL |
| `selected` | boolean; explicitly configured in this policy |
| `effectiveSeverity` | `info`, `warning`, `error`, or null for deselected rules |
| `scope` | `solution`; the engine scope whose availability is described |
| `available` | boolean; implementation registered in the shared CLI solution engine, independent of selection; not a claim that an MCP scan tool exists |

### `get_architecture`

Arguments: required `inputPath` (`.sln`, `.slnx`, `.csproj`) and `policyPath`;
optional `module`, `layer`, `configuration` (default `Debug`), `targetFramework`
(required for multi-targeted inputs, as for CLI). Enable `Workspace:Trusted` in
operator configuration to authorize build evaluation. Example:

```json
{"inputPath":"ArchitectureGuardian.slnx","policyPath":"architecture.guardian.yaml","module":"Guardian","layer":"Core","configuration":"Release"}
```

The policy directory must be above the input and every project, matching CLI
inspection. Full inventory validation happens before filtering. Combined module/layer
filters intersect project membership. Module/layer entries then describe that subset;
contract IDs are local to their module. All outgoing permissions/restrictions are
retained on returned scopes, even when their targets are outside the filter. All
operational issues and rule settings are retained. An empty valid intersection is an
empty view, with status still describing the complete underlying load/normalization.

| Response field | Type and meaning |
| --- | --- |
| `schemaVersion` | integer, currently 1 |
| `status` | `completed`, `incomplete`, `failed`, or `cancelled`; underlying normalization coverage, not policy compliance |
| `scope` | `repository`; architecture inspection, without rule evaluation |
| `policyPath`, `inputPath` | strings; paths relative to operator root |
| `configuration`, `targetFramework` | requested build configuration and explicit framework (nullable for the input default) |
| `module`, `layer` | requested filters, nullable |
| `issues` | array of `{code: string, message: string}` operational issues, independent of filters |
| `modules` | array of `{id, projects: string[], contracts: {id, projects: string[]}[], allowedDependencies: string[], forbiddenAssemblies: string[], forbiddenNamespaces: string[]}` |
| `layers` | array of `{id, projects: string[], allowedDependencies: string[], forbiddenAssemblies: string[], forbiddenNamespaces: string[], domainInfrastructure}` |
| `layers[].domainInfrastructure` | null or `{forbiddenAssemblies: string[], forbiddenNamespaces: string[], allowedNamespaces: string[]}` for AG006 |
| `projects` | array of `{path: string, module: string, layer: string, contract: string or null}` resolved ownership; paths relative to policy directory |
| `boundaries` | string array explaining direct AG001/AG002 permissions, independent AG004 whole-project contract boundaries and AG005/AG006 matching/exception semantics |
| `rules` | complete `Rule[]` catalog with configured selection and severity |
| `failOn` | configured inclusive gate threshold; no gate is evaluated |

Module/layer IDs and contracts come from the resolved Core model. Outgoing module
permissions use `module:ID` / `contract:MODULE/ID`; layer permissions use layer IDs.
Permissions are direct and directional, with implicit same-module/same-layer access.
AG004 still independently requires a public contract for cross-module semantic uses.
Restrictions and exceptions remain attached to the source module/layer. This compact
representation avoids expanding an all-project-pairs permission matrix.

Incomplete work can retain usable modules/projects, with `issues` explaining missing
coverage. If normalization cannot produce valid ownership, `modules`, `layers` and
`projects` are empty and status remains noncompleted. Rule metadata, gate settings
and boundary meanings remain available, never a substitute for resolved ownership.
Operational issue messages can contain workspace/compiler paths and diagnostics.

### `analyze_solution`

Required arguments: `inputPath` and `policyPath`, with the same path/trust rules as
`get_architecture`. Optional: `configuration` (default `Debug`) and `targetFramework`
(required for multi-targeted projects). This always analyzes the complete input;
there are no diagnostic/module filters that could hide a coverage or gate failure.

```json
{"inputPath":"ArchitectureGuardian.slnx","policyPath":"architecture.guardian.yaml","configuration":"Release"}
```

The schema below is shared with `analyze_changes` and stays at version 1 while
comparison remains unavailable:

| Response field | Type and meaning |
| --- | --- |
| `schemaVersion` | integer, currently 1 |
| `status` | `completed`, `incomplete`, `failed`, `cancelled`, or `unsupported` |
| `scope` | `solution`; full architecture context, including project input when supplied |
| `inputPath`, `policyPath` | operator-root-relative paths |
| `configuration`, `targetFramework` | requested configuration and nullable explicit framework |
| `baseReference` | null for a full scan; explicit requested ref for a comparison |
| `gateOutcome` | `passed` or `failed` only for completed analysis; otherwise `not-evaluated` |
| `failOn` | configured inclusive severity threshold (`info`, `warning`, `error`) |
| `counts` | `{total, info, warning, error}` integers for all available diagnostics; null for unsupported comparison |
| `diagnostics` | complete array of `{id, ruleId, severity, message, location, target, evidence, remediation, documentationUrl}` |
| `issues` | array of `{code, message, ruleId, location}` coverage issues; rule/location may be null |

Diagnostic IDs, evidence order, source spans and policy-assigned severity come from
the shared CLI engine. `target` contains nullable `module`/`symbol` identifiers.
`evidence` entries contain `description` and nullable `location`. Locations contain
`path`, `startLine`, `startColumn`, `endLine`, `endColumn`: one-based inclusive start,
exclusive end; project/source paths are relative to the policy directory. Rule
metadata supplies remediation and documentation. Enum values are lowercase strings.

`completed` describes coverage, independently of compliance: a completed run can
fail its gate, or pass with warnings below `failOn`. Incomplete/failed results retain
available findings and issues with no gate decision, even if counts are zero.
Requested cancellation follows SDK cancellation instead of returning a successful
tool payload. Oversized output is an explicit tool error with no structured result;
use a smaller complete input/policy scope. Findings and evidence are never truncated.

### `analyze_changes`

Required arguments: `inputPath`, `policyPath`, `baseReference`, and `scope`.
Optional: `configuration` and `targetFramework`, as above. The only defined scope
is the exact string `solution`; it reserves full-context graph comparison rather
than changed-line filtering. A base reference must be nonblank and at most 256
characters. Omission of either base or scope is an error.

```json
{"inputPath":"ArchitectureGuardian.slnx","policyPath":"architecture.guardian.yaml","baseReference":"refs/heads/main","scope":"solution"}
```

Until [F23](https://github.com/kbourzayq/arch-guard/issues/23), valid calls return
`status: "unsupported"`, `gateOutcome: "not-evaluated"`, `counts: null`, empty
`diagnostics`, and an issue with code `change-analysis-unsupported`. This structured
capability response is delivered with `isError: false`; callers must inspect status
and gate outcome. Invalid arguments, paths or policy produce tool errors instead.

The tool validates explicit paths and policy and echoes the requested base/build
context. It does not resolve refs, run Git, load workspaces, restore, or execute
build logic, so it needs no build trust. It never substitutes a full scan, zero-change
claim or passing gate. Ref existence, shallow clones, policy changes, removed/renamed
projects, staged/unstaged/untracked files and dirty-tree semantics will be defined
and verified by F23's complete base/candidate snapshot comparison.
