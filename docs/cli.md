# CLI commands

Use the CLI to scan architecture, inspect resolved ownership, explain rules and
validate workspace coverage. Install the [CLI package](install.md#install-the-cli) first. Examples use the
installed `archguard` command from your target repository root. Replace
`path/to/...` with your actual input and policy paths; quote paths containing spaces.

```sh
archguard --help
archguard scan --help
archguard --version
```

No arguments also prints help. Commands below run in your repository after installing
the CLI.

## Command reference

| Command | Required arguments | Purpose |
| --- | --- | --- |
| `scan` | `<input> --policy <path>` | Evaluate selected rules and the severity gate |
| `baseline update` | `<input> --policy <path> --baseline <path>` | Explicitly replace accepted debt after completed analysis |
| `inspect` | `<input> --policy <path>` | List resolved ownership and policy constraints |
| `load` | `<input>` | Validate workspace coverage without rule evaluation |
| `explain` | `<rule-id>` | Print local rule metadata without loading a workspace |
| `init` (development source; next feature release) | `--discover <input-or-directory>` | Write review-only policy intentions and a separate discovery report |
| `doctor` (development source; next feature release) | `<input> --policy <path>` | Diagnose installation/configuration health without certifying architecture compliance |

`scan`, `baseline update`, `inspect` and `load` also accept `--trusted`, `--configuration` (default
`Debug`) and `--framework`. Full scans do not restore automatically; change
comparison prepares temporary snapshots with cache-only restore. Development-source
`init --discover` is documented in [discovery and policy proposals](discovery.md);
it is not in the published 0.3.0 CLI. Trusted discovery requires explicit
`--configuration`, accepts `--framework` and `--output-directory`, and never adopts policy.

## Diagnose installation and policy health

`doctor` is available in development source and the next feature release; it is not
in the published 0.3.0 CLI. Choose an explicit solution/project and policy:

```sh
archguard doctor path/to/Application.slnx --policy path/to/architecture.guardian.yaml
archguard doctor path/to/Application.slnx --policy path/to/architecture.guardian.yaml --trusted --configuration Release --framework net10.0 --format json
```

Without `--trusted`, doctor only reads paths, YAML and literal XML, runs the .NET
host's `--version`/`--list-runtimes` metadata queries, and checks executable presence
on PATH. SDK selection honors `global.json` at the input directory. Root
`Directory.Build.props`/`Directory.Packages.props` and default `obj/project.assets.json`
paths are **unevaluated hints**: imports, conditions and custom asset paths can
change them. Literal inactive/missing references are warnings, not asserted owners
or required restore failures. Missing SDK/runtime and invalid YAML have concrete
remediation. No project is evaluated, no generator runs, and no restore/install is
performed. Evaluated checks are marked `notRun`, coverage is incomplete, and exit is 2.

After reviewing repository build logic, `--trusted` authorizes the existing workspace
loader, including MSBuild tasks and source generators. It requires explicit
`--configuration`; multi-targeting requires `--framework`. Repeat doctor separately
for each relevant context. Doctor still never requests restore or tool installation.
Repository-controlled build logic can have side effects; trust is not isolation.

Evaluated checks retain partial contexts and original workspace issues/status. They
use the existing ownership resolver for unmatched/stale selectors and projects with
missing/ambiguous module, layer or contract ownership, relative to the policy directory.
The resolver reports its first mapping error; fix it and rerun for subsequent errors.
No policy is synthesized or adopted. A discovery hint never grants permissions.

`--workflow scan` is the default. `--workflow compiler` requires a loaded analyzer
package, valid CPM/non-CPM registration, enabled `RunAnalyzers`/`RunAnalyzersDuringBuild`,
and exactly one readable policy input from the compiler's actual post-target
AdditionalFiles. YAML must match `--policy` and include package build-target project
metadata. Supported [legacy XML wiring](compiler-analyzer.md#compiler-policy-snapshot) is accepted, with a separate optional
`legacy-policy-validation: notRun` check: XML schema, assembly mappings and parity
with the explicit YAML require an independent compiler build. XML mode selects all
supported compiler rules with warning defaults, then applies host overrides.
The evaluated imported props/package paths
help find nested props that hide root installation. `--workflow mcp` additionally
requires `archguard-mcp` on this process's PATH; clients using an absolute executable
or manifest must expose it on PATH for this check. The server is never launched and
client connectivity is not tested. `--workflow ci` reports the same workspace
prerequisites as scan, plus CI preparation guidance. Checkout completeness, private
feed credentials and asset freshness require independent CI preparation.
Missing optional integrations are informational. An analyzer that is present but
has broken policy wiring is a blocker even outside the compiler workflow.

Doctor lists selected YAML rules, solution severities and gate threshold. For each
available source tree it reports the compiler's effective rule override through
the same Roslyn options provider the analyzer uses, including `none` suppression
(`suppress` in output), warnings/errors and omitted inactive rules. AG003 remains
solution-only. Compiler warning promotion can further affect build reporting.
The analyzer API floor is Roslyn 4.8 / .NET Standard 2.0; doctor cannot inspect an
interactive IDE's host version. CLI/MCP need .NET 10. SDK selection alone does not
certify SDK/workload/compiler compatibility; trusted loading tests the chosen context.

Text and `--format json` report the same checks on stdout, including blockers;
stderr is reserved for argument/startup failures. JSON has the
[doctor v1 schema](schema/doctor.v1.schema.json), with:

| Field | Meaning |
| --- | --- |
| `schemaVersion`, `kind` | `1`, `architecture-doctor` |
| `status` | `completed`, `incomplete`, `failed` or `cancelled` diagnostic coverage |
| `workspaceStatus` | Original workspace status, or `null` when evaluation did not run |
| `exitCode` | `0` for completed coverage without required blockers/not-run checks; otherwise `2` |
| `compliance` | Always `notEvaluated`; never a gate pass |
| `requested` | Absolute input/policy, workflow, trust, configuration and nullable framework |
| `exploredContexts` | Actual project/configuration/framework and declared framework choices; partial work is retained |
| `checks` | Ordered checks containing `id`, `scope`, `mode`, `status`, `required`, `message`, `remediation` |

Check `mode` is `static` or `evaluated`; check `status` is `passed`, `informational`,
`warning`, `blocked` or `notRun`. `id` plus `scope` identifies a check; multiple
workspace issues can share these values and retain distinct messages. Consumers
should accept new IDs and display remediation rather than parse message text.
Checks sort ordinally by ID, scope and message; contexts sort by project path. JSON
uses LF with a final newline and no timestamps/timings, so repeated runs over the
same input/environment are stable. Reports contain local paths and host diagnostics;
redact them before sharing. Doctor writes no report file or source/policy/baseline.
Redirect stdout to a separate report location when needed.

Cancellation during diagnosis retains a `cancelled` report and exit 2. Parsing or
cancellation before command execution can return stderr and exit 2 without JSON.
Exit 1 is reserved for a completed **scan** whose architecture gate failed.
A completed diagnostic report can contain blockers; always inspect `exitCode` and
required checks as well as coverage.

Environment/configuration health does not imply architecture compliance. Establish
that separately after explicit restore, using the same input and context:

```sh
archguard scan path/to/Application.slnx --policy path/to/architecture.guardian.yaml --trusted --configuration Release --framework net10.0
```

## Scan a solution or project

Restore the input explicitly before scanning. Workspace loading performs no restore.
Trust is required because MSBuild can execute repository build tasks. Isolate
untrusted repositories before authorizing them, as described in
[workspace loading](workspace-loading.md).

```sh
dotnet restore path/to/Application.slnx -p:Configuration=Release
archguard scan path/to/Application.slnx --policy path/to/architecture.guardian.yaml --trusted --configuration Release
```

Inputs may be `.sln`, `.slnx` or `.csproj`. Project input includes the referenced
projects loaded by the workspace. `--policy` is required and explicit: there is no
upward search or fallback policy. All input/project identities are relative to the
directory containing that file, so place it above the complete input inventory.
Selectors must cover exactly that scope, including referenced projects. See the
[policy guide](policy.md). Paths containing spaces should be quoted by the shell.

`--configuration` defaults to `Debug`; `--framework <tfm>` is required for
multi-targeted projects. Coverage applies to that configuration and framework.
`scan` registers AG001–AG006 and runs only policy-selected IDs. AG006 enforces
layer-scoped domain technology restrictions and namespace exceptions; see the
[policy guide](policy.md#domain-infrastructure-ag006).

Each finding prints severity, canonical rule ID, message, source location when
available, module/symbol target, deterministic diagnostic ID, supporting evidence,
remediation and documentation URL. Finding order comes from the shared analysis
service. All findings, including gate-failing errors, go to stdout; operational
problems go to stderr. Available findings survive incomplete analysis. A final
completed summary states the diagnostic count and gate outcome only when coverage
is complete.

| Exit | Meaning |
| --- | --- |
| `0` | Command completed; scan's configured severity gate passed |
| `1` | Scan completed; the configured severity gate failed |
| `2` | Invalid arguments/configuration, unavailable rules, loading or analysis failure, incomplete coverage, or cancellation |

A warning may pass an error-threshold gate. An incomplete scan with zero findings
still returns 2. Missing assets/SDKs, compiler failures and policy mapping errors
include actionable context on stderr. Ctrl+C requests cooperative cancellation
through policy loading, workspace integration, extraction and evaluation; cancellation
returns 2 and never claims a completed gate result.

## Export JSON or SARIF reports

`scan --format json` emits the version-one JSON report; `--format sarif` emits
SARIF 2.1.0. Both write one complete document to stdout. Add `--output <path>` to
write a UTF-8 file instead; its parent directory must exist. `--output` requires
one of these machine formats. The default `--format text` preserves console output.
These options apply only to `scan`, including `--baseline` and `--base` scans.

```sh
archguard scan path/to/Application.slnx --policy path/to/architecture.guardian.yaml --trusted --format json
archguard scan path/to/Application.slnx --policy path/to/architecture.guardian.yaml --trusted --format sarif --output architecture.sarif
```

Machine output includes available findings and explicit completion and gate status,
including partial analysis. Operational explanations remain on stderr. File-write
failures return `2`, including when analysis completed successfully. See the
[report guide](reports.md) for schemas, baseline state, URI normalization and
cancellation behavior.

## Baselines and new-violation gates

`scan --baseline <path>` reads an explicit JSON inventory, reports existing/new/resolved
findings and gates only new findings. `baseline update <input> --policy <path>
--baseline <path> --trusted` explicitly replaces that inventory after completed
analysis, returning `0` for a successful write. Scans never update it. Coverage
failures still return `2`. See [baselines](baselines.md) for versioning, movement,
rename and ambiguous-match behavior, and review requirements.

## Compare changes against a Git base

`scan <input> --policy <path> --base <commit-or-ref> --trusted` evaluates the
complete base and working-tree architecture contexts and gates only new findings.
It includes staged, unstaged and non-ignored untracked files. Both snapshots must
complete before the command returns `0` or `1`; unavailable refs or context return
`2`. `--base` and `--baseline` are mutually exclusive. See [change analysis](changes.md)
for exact Git, policy, rename, offline preparation and trust semantics.

## Inspect resolved boundaries

```sh
archguard inspect path/to/Application.slnx --policy path/to/architecture.guardian.yaml --trusted
```

`inspect` accepts the same input, trust, configuration and framework options. It
loads and normalizes the workspace, then lists modules, exact member project paths,
layer/contract ownership, named public contracts, permitted outgoing module/contract
and layer dependencies, selected rule severities and the gate threshold. Missing
cross-boundary permission means denial; same-module and same-layer dependencies
pass their respective checks. Permissions remain direct and directional.

Inspection evaluates no architecture rules and returns 0 only for complete
normalization. If a partial model is available it is printed alongside coverage
errors on stderr and exit 2. A valid policy can be inspected even when it selects
future rules: inspection reports declarations rather than implementation availability.

## Explain a canonical rule

```sh
archguard explain AG001
```

`explain` resolves AG001–AG006 case-sensitively from the local catalog, prints the
title and documentation URL, and states whether the current scan engine implements
it. It requires no workspace, trust, policy or network. Unknown IDs return 2 with
corrective guidance. Documentation links are not fetched automatically.

## Validate workspace loading

`load <input> --trusted [--configuration <name>] [--framework <tfm>]` remains
available. It reports workspace coverage without policy resolution or architecture
evaluation, returns 0 for complete loading and 2 for incomplete, failed or cancelled
loading. See [workspace loading](workspace-loading.md) for the adapter contract.

## Interpreting operational failures

Exit `2` means the selected context could not establish reliable compliance.
Keep its issues visible, fix the reported input/build problem and rerun. See
[known limitations](troubleshooting.md#known-analysis-limitations),
[engine coverage](semantic-accuracy.md) and [performance and telemetry](performance.md).
