# CLI commands

Use the CLI to scan architecture, inspect resolved ownership, explain rules and
validate workspace coverage. Follow [installation](installation.md) first.
Examples below run from the Architecture Guardian checkout root and use the
published CLI in `artifacts/cli`. When scanning another repository, replace
`path/to/...` with your input and policy paths; quote paths containing spaces.

```sh
dotnet artifacts/cli/ArchitectureGuardian.Cli.dll --help
dotnet artifacts/cli/ArchitectureGuardian.Cli.dll scan --help
dotnet artifacts/cli/ArchitectureGuardian.Cli.dll --version
```

No arguments also prints help. The intended tool name is `archguard`; packaged
tool installation is planned, so generated help currently uses the assembly name.
For development, `dotnet run --project src/ArchitectureGuardian.Cli -- <arguments>`
uses the same commands.

## Command reference

| Command | Required arguments | Purpose |
| --- | --- | --- |
| `scan` | `<input> --policy <path>` | Evaluate selected rules and the severity gate |
| `baseline update` | `<input> --policy <path> --baseline <path>` | Explicitly replace accepted debt after completed analysis |
| `inspect` | `<input> --policy <path>` | List resolved ownership and policy constraints |
| `load` | `<input>` | Validate workspace coverage without rule evaluation |
| `explain` | `<rule-id>` | Print local rule metadata without loading a workspace |

`scan`, `baseline update`, `inspect` and `load` also accept `--trusted`, `--configuration` (default
`Debug`) and `--framework`. Full scans do not restore automatically; change
comparison prepares temporary snapshots with cache-only restore. There is no policy discovery.

## Scan a solution or project

Restore the input explicitly before scanning. Workspace loading performs no restore.
Trust is required because MSBuild can execute repository build tasks. Isolate
untrusted repositories before authorizing them, as described in
[workspace loading](workspace-loading.md).

```sh
dotnet restore path/to/Application.slnx -p:Configuration=Release
dotnet artifacts/cli/ArchitectureGuardian.Cli.dll scan path/to/Application.slnx --policy path/to/architecture.guardian.yaml --trusted --configuration Release
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
dotnet artifacts/cli/ArchitectureGuardian.Cli.dll scan path/to/Application.slnx --policy path/to/architecture.guardian.yaml --trusted --format json
dotnet artifacts/cli/ArchitectureGuardian.Cli.dll scan path/to/Application.slnx --policy path/to/architecture.guardian.yaml --trusted --format sarif --output architecture.sarif
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
dotnet artifacts/cli/ArchitectureGuardian.Cli.dll inspect path/to/Application.slnx --policy path/to/architecture.guardian.yaml --trusted
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
dotnet artifacts/cli/ArchitectureGuardian.Cli.dll explain AG001
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

## Current repository scan limitation

The repository's self-scan can return incomplete coverage for known extraction
and workspace issues. The CLI preserves available findings without claiming a
gate outcome. See [known analysis limitations](troubleshooting.md#known-analysis-limitations)
for details; use the [reference examples](m1-demo.md) for reproducible command outcomes.
