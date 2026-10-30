# MCP implementation and validation

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

## Verification

`Mcp/CorrectionWorkflowTests` launches the production DLL with the SDK's
`StdioClientTransport`. It discovers the five read-only tools, reads architecture,
finds the fixture's seeded AG001, requests an instance explanation, corrects source
and references through test-side file access, restores externally and verifies a
completed passing rescan. Each tool call compares all fixture input bytes before
and after, excluding `bin/` and `obj/`, including stale-ID and completed Git
comparison calls. It also verifies CLI comparison parity. The client captures
stderr and has bounded startup,
session and SDK shutdown timeouts. It uses an isolated copy; checked-in fixture
inputs remain unchanged. This deterministic protocol test requires no LLM or
coding-agent subscription and runs in the normal cross-platform CI matrix.

After the standard Release build, reproduce it with:

```sh
dotnet test tests/ArchitectureGuardian.ArchitectureTests --configuration Release --no-build --no-restore --filter FullyQualifiedName~CorrectionWorkflowTests
```

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
inputs, severity gates, evidence, required comparison base/scope,
trust/input errors, oversized diagnostic output and pre-cancelled calls.
`ToolRegistrationTests` also cancels both analysis tools during policy loading
through an explicit protocol notification.

`Mcp/ExplanationToolTests` checks all six rule explanations, exact scan/instance
parity, reference-only and semantic evidence, configured contracts, fresh stale-ID
errors, incomplete coverage, bounds and cancellation. Core `ViolationContextTests`
checks evaluator provenance for all six rules, directed cycle order, immutable
snapshots and preservation of v1 identities.

`Changes/ChangeAnalysisTests` compares shared/CLI/MCP results against independent
full scans, including imported references between unchanged projects, staged,
unstaged and untracked source, policy-only changes, removed/renamed projects,
existing debt, missing refs/shallow history, invalid base policy, failed restore,
missing inputs and incomplete base/candidate compilation.

For source-development measurements and opt-in local phase/rule metrics, see
[performance budgets and telemetry](performance.md).

### Publish the server

For local development, follow [source installation](installation.md#install-from-source).
Replace `command` in a client example with the absolute `dotnet` executable path
and prepend the absolute published `artifacts/mcp/ArchitectureGuardian.Mcp.dll`
path to `args`. Retain the workspace options. Keep the complete publish directory.
`dotnet run` can add build output to stdout and interfere with MCP framing.
