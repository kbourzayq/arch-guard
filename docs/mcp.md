# MCP server

Connect an MCP-compatible coding agent to your repository's architecture policy
and solution analysis. The server runs locally over stdio and shares the CLI's
policy, workspace and rule engine. It registers five read-only tools:

| Tool | Purpose | Build trust required |
| --- | --- | --- |
| `list_rules` | Read rule selection, effective severities and engine availability from YAML | No |
| `get_architecture` | Resolve modules, layers, ownership, public contracts and constraints | Yes |
| `analyze_solution` | Scan the complete input and return coverage, gate, counts and evidence | Yes |
| `explain_violation` | Explain a rule or resolve a current diagnostic with evidence, path, policy and remediation | Diagnostic mode only |
| `analyze_changes` | Compare full Git base/candidate contexts and gate new findings | Yes, for both trees |

No prompts or resources are registered. Source/policy/baseline mutation and
arbitrary shell execution are outside the server's capabilities. Full scans
require restore outside MCP; `analyze_changes` prepares isolated snapshots with
cache-only restore and does not download packages. Trusted workspace evaluation
can execute repository-controlled MSBuild tasks.

## Start and connect

### Install the packaged server

Install the published .NET tool from a directory where `dotnet --version` selects
SDK 10. If your repository pins an older SDK in `global.json`, install from outside
that repository; its own SDK selection still applies when restoring and analyzing it:

```sh
dotnet tool install --global ArchitectureGuardian.Mcp --version 0.3.0
```

The machine needs .NET 10 and the SDK required by the repository being loaded.
Keep CLI, MCP and analyzer versions aligned. MCP reports its package version in
its initialize response; it is a stdio service, not a web server or interactive CLI.
The [installation guide](install.md#install-tools-only-for-one-repository) also covers a
repository-local tool directory, updates and removal.

Find the absolute executable path with `command -v archguard-mcp` on macOS/Linux
or `(Get-Command archguard-mcp).Source` in PowerShell. Global tools normally live
under `$HOME/.dotnet/tools` or `%USERPROFILE%\.dotnet\tools`. If command discovery
fails, fix PATH or resolve the shim in that directory. Windows uses
`archguard-mcp.exe`. The client launches the server and owns its stdin/stdout.

### Configure your client

Replace these markers in the examples below:

| Marker | Value |
| --- | --- |
| `<FULL_PATH_TO_ARCHGUARD_MCP>` | Absolute path to the installed `archguard-mcp` executable |
| `<FULL_PATH_TO_YOUR_REPOSITORY>` | Existing absolute root of the repository to analyze |

Markers are not environment variables. The server does not expand `~`, `$HOME`,
`%USERPROFILE%` or `${...}` in workspace paths. Do not depend on a client's current
directory. Keep personal absolute paths in local configuration. In JSON, Windows
paths may use forward slashes or escaped backslashes (`\\`).

#### Codex CLI, desktop and IDE extension

Merge this into local `~/.codex/config.toml`, preserving existing settings:

```toml
[mcp_servers.architecture-guardian]
command = '<FULL_PATH_TO_ARCHGUARD_MCP>'
args = ["--Workspace:Root", '<FULL_PATH_TO_YOUR_REPOSITORY>', "--Workspace:Trusted", "false"]
tool_timeout_sec = 180
```

Single-quoted TOML paths preserve Windows backslashes. If a path contains an
apostrophe, use a double-quoted string with forward slashes instead.

Alternatively, register from a terminal:

```sh
codex mcp add architecture-guardian -- "<FULL_PATH_TO_ARCHGUARD_MCP>" --Workspace:Root "<FULL_PATH_TO_YOUR_REPOSITORY>" --Workspace:Trusted false
```

CLI registration uses the default tool timeout until you configure it; the TOML
example allows three minutes for workspace operations. Run `codex mcp list`, start
a new session and inspect `/mcp` for active tools. Registration alone does not
prove a connection. These clients share configuration; see the official
[Codex MCP documentation](https://learn.chatgpt.com/docs/extend/mcp).

#### Claude Code

For private project-local registration, run:

```sh
claude mcp add --transport stdio --scope local architecture-guardian -- "<FULL_PATH_TO_ARCHGUARD_MCP>" --Workspace:Root "<FULL_PATH_TO_YOUR_REPOSITORY>" --Workspace:Trusted false
```

Local scope stores the entry for this project in `~/.claude.json`. Alternatively,
merge this into project-root `.mcp.json` under your client's applicable trust rules:

```json
{
  "mcpServers": {
    "architecture-guardian": {
      "type": "stdio",
      "command": "<FULL_PATH_TO_ARCHGUARD_MCP>",
      "args": ["--Workspace:Root", "<FULL_PATH_TO_YOUR_REPOSITORY>", "--Workspace:Trusted", "false"]
    }
  }
}
```

Keep personal paths out of version control. Check `claude mcp get architecture-guardian`
and `/mcp` in an interactive session to confirm connection and tool discovery.
See the official [Claude Code MCP documentation](https://code.claude.com/docs/en/mcp).

#### GitHub Copilot CLI

Merge this into `~/.copilot/mcp-config.json`:

```json
{
  "mcpServers": {
    "architecture-guardian": {
      "type": "stdio",
      "command": "<FULL_PATH_TO_ARCHGUARD_MCP>",
      "args": ["--Workspace:Root", "<FULL_PATH_TO_YOUR_REPOSITORY>", "--Workspace:Trusted", "false"],
      "tools": ["list_rules", "get_architecture", "analyze_solution", "explain_violation", "analyze_changes"]
    }
  }
}
```

Alternatively, use `/mcp add`, choose STDIO and enter the same command, arguments
and tools. Inspect `/mcp` for status and discovery. This is a Copilot CLI format;
VS Code's `.vscode/mcp.json` uses a different `servers` wrapper. Enterprise policies
can restrict servers. See the official
[Copilot CLI MCP documentation](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers).

No Architecture Guardian API key is needed. Client accounts, model access and
organization policies follow the client's requirements. Automated tests exercise
the MCP SDK and installed server, not authenticated interactive sessions in each
agent. Verify your actual client's discovery before relying on workspace analysis.

### Enable workspace analysis

The example starts with build trust disabled. `list_rules` works in that mode;
`get_architecture` and `analyze_solution` return incomplete coverage with a
`workspace-untrusted` issue.

For a repository whose build logic you authorize:

1. Restore the target solution through your terminal or agent's ordinary command tools, in its intended build configuration, rather than through MCP.
2. Change the final `Workspace:Trusted` argument from `false` to `true`.
3. Restart or reconnect the server through your client.

Trust authorizes repository MSBuild tasks and generators; read-only tool annotations
and root checks do not provide a build sandbox. The [workspace trust contract](workspace-loading.md#trust-and-isolation)
also applies to restore.

### Make the first tool call

Tool paths are relative to `Workspace:Root`, independent of where the server executable
is installed. For a repository with `MyApp.slnx` and `architecture.guardian.yaml`
at its root:

```json
{"policyPath":"architecture.guardian.yaml"}
```

Pass that object to `list_rules`. With build trust enabled and the input restored,
call `analyze_solution` with:

```json
{"inputPath":"MyApp.slnx","policyPath":"architecture.guardian.yaml","configuration":"Release"}
```

Check `status` and `gateOutcome` before interpreting counts. An incomplete result
is not a passing scan. See the [response schemas](#tool-requests-and-response-schemas-version-1)
for every argument and result field, or [troubleshooting](troubleshooting.md#mcp-connection-and-path-problems)
for connection and path failures.

### Configuration and lifecycle

The standard .NET host reads command-line configuration, environment variables
and configuration files. Environment keys are `Workspace__Root` and
`Workspace__Trusted`. `Workspace:Root` must be an existing absolute directory.
Invalid settings fail startup with a nonzero exit code and stderr diagnostics.
The SDK owns stdin/stdout; console logging goes to stderr. Git/SDK subprocesses
receive closed, isolated stdin rather than inheriting the live protocol pipe.
The stdio Git regression keeps that pipe open across repeated requests, including
on Windows, where native Git startup inspects inherited pipe handles.

The server uses ModelContextProtocol C# SDK 2.3.0 with standard .NET hosting and
stdio transport. It negotiates protocol versions, including the `2025-11-25`
handshake. Closing stdin ends the session; host shutdown and Ctrl+C use cooperative
cancellation. No HTTP endpoint or web dashboard is provided.

## Coding-agent correction workflow

For a reusable version of this workflow, install the optional
[architecture-guardian skill](agent-workflow.md). It supports MCP and CLI fallback.

Keep module ownership, dependency permissions, contracts, layers and severity in
`architecture.guardian.yaml`. Client instructions describe the workflow and point
to that policy; do not copy its architecture constraints into `AGENTS.md`,
`CLAUDE.md` or Copilot instructions. For example:

```text
Use Architecture Guardian to inspect the configured architecture before editing.
Read architecture.guardian.yaml as the source of architecture constraints. Scan
the complete solution in the intended build context, then explain an exact returned
diagnostic ID before correcting its dependency. Make edits with ordinary file
tools and rescan. Report coverage, gate outcome and remaining issues. Run the
repository's normal build/tests and CI; agent validation does not replace them.
```

After restoring and explicitly enabling build trust, use this sequence. Substitute
your real input and preserve configuration/framework across calls:

1. Call `list_rules` with `{"policyPath":"architecture.guardian.yaml"}`.
2. Call `get_architecture` and then `analyze_solution` with
   `{"inputPath":"MyApp.slnx","policyPath":"architecture.guardian.yaml","configuration":"Release"}`.
   Inspect ownership, configured public contracts and permissions. A scan is usable
   for compliance only with `status: "completed"`; inspect `gateOutcome` separately.
3. Copy an actual diagnostic's `id` into `explain_violation`, alongside the same
   input, policy, configuration and optional target framework. Do not synthesize
   an ID or submit invented evidence. Confirm its evidence, dependency path and
   violated constraint. Configured contracts are candidates to inspect, not a
   guarantee of API suitability or permission.
4. Correct source and references through ordinary client file tools. In the
   [downloadable modular-monolith sample](use-cases.md#modular-monolith-boundaries),
   the AG001 context uses another module's application implementation. Replace the
   dependency with an appropriate existing contract and remove the implementation
   project reference. Keep policy unchanged and restore the same context again.
5. Rerun `analyze_solution` with the same arguments. Require `completed`, a passed
   gate and no operational issues. Explain any remaining findings with IDs from
   the fresh scan; old IDs can become stale after a correction.

The server has no source, policy or baseline write tools or shell tool. Full scans
require explicit restore; change comparison prepares temporary snapshots with
cache-only restore. The agent's file/command tools perform edits and restores
under their own permissions. Trusted MSBuild evaluation can execute build logic and create `bin/`
and `obj/` artifacts; read-only annotations are not a build sandbox. Resolve any
incomplete, failed or cancelled analysis before claiming the correction passed.

For working-tree comparisons, call `analyze_changes` against an explicit release
base and require `completed` coverage and a `passed` new-finding gate. It includes
staged, unstaged and non-ignored untracked files with each tree's full architecture
context and policy. Keep full scans and normal CI mandatory because existing debt
can pass a comparison. See [change analysis](changes.md) and the
[full-scan and CI guidance](ci.md#choose-the-accepted-debt-policy-explicitly).

## Workspace, cancellation, limits and errors

The operator selects one existing absolute workspace directory. Build trust defaults to false;
set `--Workspace:Trusted true` or `Workspace__Trusted=true` to authorize
workspace execution. Client roots cannot change these settings, and the host does
not request client roots. `list_rules` and rule-mode `explain_violation` read policy without build trust.
`get_architecture` and `analyze_solution` pass the operator
trust setting to the shared loader; diagnostic-mode `explain_violation` does the same; a client cannot enable it per call.

Explicit policy/input paths are resolved relative to the operator root, must name
existing files inside it, and cannot traverse symbolic links or reparse points
below that root. Absolute paths inside the root are accepted. The configured root
itself is operator-owned. These checks are preflight validation: concurrent file
replacement is outside the contract. The root does not sandbox trusted MSBuild,
its imports, referenced projects, generators or SDK discovery. Trust only repositories
whose build logic you authorize. The existing [workspace trust contract](workspace-loading.md),
full-scan no-restore behavior and one-SDK-per-process limitation apply. Change
comparison prepares temporary snapshots with cache-only restore; incompatible SDK
contexts still block coverage.

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
Diagnostic-mode `explain_violation` preserves the same coverage/issue and trust contracts.
Requested cancellation is propagated to SDK cancellation even though the shared
workspace services internally represent it as a coverage status. Invalid policies,
unknown filters and oversized output produce tool errors, without partial payloads.
Unexpected exceptions retain the SDK generic error message.

## Tool requests and response schemas (version 1)

All tools return the same JSON object in `structuredContent` and one text block
for clients without structured-content support. Use `tools/list` to discover each
tool's `inputSchema` and `outputSchema`. Response contracts carry `schemaVersion`.
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
{"inputPath":"MyApp.slnx","policyPath":"architecture.guardian.yaml","configuration":"Release"}
```

The schema below is shared with `analyze_changes` at version 1; `comparison` is an
additive nullable field:

| Response field | Type and meaning |
| --- | --- |
| `schemaVersion` | integer, currently 1 |
| `status` | `completed`, `incomplete`, `failed`, or `cancelled` |
| `scope` | `solution`; full architecture context, including project input when supplied |
| `inputPath`, `policyPath` | operator-root-relative paths |
| `configuration`, `targetFramework` | requested configuration and nullable explicit framework |
| `baseReference` | null for a full scan; explicit requested ref for a comparison |
| `gateOutcome` | `passed` or `failed` only for completed analysis; otherwise `not-evaluated` |
| `failOn` | configured inclusive severity threshold (`info`, `warning`, `error`); `unavailable` if candidate policy could not load |
| `counts` | `{total, info, warning, error}` integers for all available candidate diagnostics |
| `diagnostics` | complete array of `{id, ruleId, severity, message, location, target, evidence, remediation, documentationUrl}` |
| `issues` | array of `{code, message, ruleId, location}` coverage issues; rule/location may be null |
| `comparison` | null for full scans or an unavailable base; otherwise `{baseCommit, existing, new, resolved}` |

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
is the exact string `solution`, selecting full-context graph comparison. A base
reference must be nonblank and at most 256 characters. Omission of either base or
scope is an error.

```json
{"inputPath":"MyApp.slnx","policyPath":"architecture.guardian.yaml","baseReference":"refs/heads/main","scope":"solution"}
```

The exact base commit is resolved locally; no merge-base selection or fetch occurs.
The candidate is the on-disk working tree, including staged, unstaged and non-ignored
untracked files. Each snapshot's policy, project inventory and graph are evaluated
independently. Only new candidate findings are gated, using stable fingerprints.
All candidate diagnostics and counts remain visible. `comparison.baseCommit` is
the resolved SHA; `existing` and `new` are candidate diagnostic-ID arrays;
`resolved` contains absent base entries with fingerprint, rule, target, dependency
path, constraints and original snapshot ID. Missing findings are not resolved from
an incomplete candidate. If the base is unavailable, `comparison` is null.

Both trees require operator build trust. `Workspace:Root` must contain the Git
root, and the input/policy paths must exist at the same relative paths in both
snapshots. Temporary snapshots use cache-only locked restore, with no package
download; prepare both trees' SDKs and package cache explicitly. Original inputs,
Git index and refs are unchanged by snapshot preparation. Read-only annotations
are not a sandbox for trusted build logic. See [change analysis](changes.md) for
policy changes, removed/renamed projects, shallow clones, snapshot limitations
and dirty-tree semantics.

Unavailable refs, policy or snapshot loads return incomplete coverage with issues
and `gateOutcome: "not-evaluated"`, delivered with `isError: false`. Invalid tool
arguments or candidate paths produce tool errors. Cancellation follows SDK semantics;
output above 65536 UTF-8 bytes is an explicit error with no truncated result.

### `explain_violation`

Required: `policyPath` and **exactly one** of `ruleId` or `diagnosticId`.
Both modes return deterministic guidance from policy and shared rule semantics;
no LLM or remote service is used.

For **rule guidance**, use a canonical case-sensitive AG001–AG006 ID:

```json
{"policyPath":"architecture.guardian.yaml","ruleId":"AG004"}
```

This reads policy only, including deselected rules, without build trust or project
selector resolution. `status: "completed"` means the policy was validated, with
`scope: "rule"`, null `diagnostic`/input/build fields and an empty `dependencyPath`.
`policyConstraints` describes the declared settings for that rule; these are not
claims that a violation or resolved inventory exists. `inputPath` is rejected in
this mode. `configuration`/`targetFramework` are validated but unused in rule mode.

For an **instance explanation**, copy the exact lowercase 64-character ID from
`analyze_solution` and reuse its input, policy and build arguments. Replace the
example ID below with an ID returned by your scan:

```json
{"policyPath":"architecture.guardian.yaml","inputPath":"MyApp.slnx","configuration":"Release","diagnosticId":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}
```

`inputPath` is required for diagnostic mode; optional `configuration` defaults to
`Debug` and `targetFramework` follows the scan contract. Workspace trust is required
for fresh analysis, which runs on every instance request. There is no session cache,
stored snapshot or ability to submit fabricated diagnostic objects as evidence.
A reference remains valid if its v1 finding content is unchanged, including across
severity-only policy changes. This ID is not a repository-wide snapshot hash.

| Response field | Meaning |
| --- | --- |
| `schemaVersion` | 1 |
| `status` | `completed`, `incomplete`, `failed` or `cancelled`; coverage, never gate compliance |
| `scope` | `rule` or `diagnostic` |
| `policyPath`, `inputPath`, `configuration`, `targetFramework` | Root-relative paths and requested build context; input/build values null in rule mode |
| `rule` | Same `Rule` metadata as `list_rules`; null if incomplete analysis cannot resolve the diagnostic |
| `explanation` | Rule semantics, the original diagnostic message, or an explanation of unresolved coverage |
| `diagnostic` | Unmodified diagnostic projection from `analyze_solution`, including all evidence and available source ranges; null in rule mode or unresolved incomplete analysis |
| `dependencyPath` | Ordered `{kind, identity}` endpoints from the evaluator; empty without an instance |
| `policyConstraints` | `{scope, setting, values: string[]}`; symbolic owning scope and relevant configured values, without fabricated YAML coordinates |
| `publicContracts` | Declared `contract:MODULE/ID` alternatives, not verified replacement APIs or permission guarantees |
| `remediation` | Rule-specific guidance, naming declared contracts for AG001/AG004 where available |
| `issues` | All operational issues from fresh analysis, or empty in rule mode |

AG001/AG002 paths identify source and target projects; AG003 returns its actual
representative directed module cycle, repeating the starting module. AG004 returns
source project, target project and bound target symbol; AG005/AG006 return source
project and bound target symbol, including external metadata. These paths describe
observed references/uses, without inventing transitive dependencies. Locations and
supporting facts remain in `diagnostic`; a project/assembly reference with no source
span stays unlocated. AG003 has a built-in acyclic-module invariant, represented by
`scope: "rule:AG003"`, `setting: "acyclicModules"`, `values: []`, rather than an
invented YAML setting. Other scopes use symbolic `modules.ID`, `layers.ID` or
`layers.ID.domainInfrastructure` labels, not parseable YAML source paths. Instance
values are the relevant allowed list, contract IDs or matched forbidden value.

Unknown rules, malformed diagnostic IDs, ambiguous mode arguments and unknown/stale
IDs after **complete** analysis return `isError: true` with actionable messages.
After incomplete/failed analysis, a found instance retains its evidence and coverage
issues; an absent instance returns that noncompleted status with null `diagnostic`
and empty path/constraint/contract arrays. Missing coverage cannot prove an ID stale.
Resolve the issues and rerun the scan. Policy changes, source changes or a different
build/input can invalidate IDs; severity alone does not change finding identity.
Configured contracts are suggestions for investigation, never fabricated replacement
symbols. Module permissions, layer permissions and API suitability remain independent.
When the target module declares no contracts, remediation states that explicitly.

Paths, policy bounds, input/build lengths, requested cancellation and the 65536-byte
payload cap follow the existing tools. Oversized evidence or guidance fails without
partial structured content. Both JSON copies and generated input/output schemas use
the same response contract. No gate is evaluated by this tool.
