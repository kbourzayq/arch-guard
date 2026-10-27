# Architecture Guardian documentation

For published CLI, MCP, analyzer and optional skill installation, start with the
[root README](../README.md). These guides cover local development, day-to-day use
and the implementation contracts behind the installed tools.

## Build and run locally

| Guide | What you will do |
| --- | --- |
| [Local development](installation.md) | Install the pinned SDK, build/publish hosts and install local packages |
| [Getting started](getting-started.md) | Define a policy, scan your repository and try the checked-in example |
| [Reference examples](m1-demo.md) | Run passing, violating and incomplete fixture configurations |
| [Contributing](../CONTRIBUTING.md) | Run required checks and prepare a focused pull request |
| [Packages and releases](releases.md) | Verify reproducible packages, version synchronization and release prerequisites |

## Use the tools

| Guide | Reference |
| --- | --- |
| [CLI](cli.md) | Commands, options, output and gate behavior |
| [MCP](mcp.md) | Installed-server setup for Codex, Claude Code and Copilot CLI; requests and responses |
| [Agent workflow](agent-workflow.md) | Install/invoke the portable skill, integrate AGENTS.md and validate corrections |
| [Compiler analyzer](compiler-analyzer.md) | Package reference, XML snapshot, severities and host compatibility |
| [Policy](policy.md) | YAML v1, ownership, contracts, layers, rule selection and editor schema |
| [Rule catalog](rule-catalog.md) | AG001–AG006 examples, evidence, remediation and limits |
| [Baselines](baselines.md) | Reviewed accepted debt, stable matching and new-finding gates |
| [Change analysis](changes.md) | Full Git comparison and working-tree semantics |
| [Reports](reports.md) | JSON v1, SARIF 2.1.0, file output and validation |
| [CI quality gates](ci.md) | Independent installed-CLI gate and complete source-based pipeline examples |
| [Troubleshooting](troubleshooting.md) | Installation, restore, coverage, multi-targeting and MCP failures |

## Understand the implementation

- [Semantic accuracy and engine parity](semantic-accuracy.md): capabilities and intentional engine differences.
- [Workspace loading](workspace-loading.md): SDK selection, restore/build contexts,
  [offline operation](workspace-loading.md#offline-and-deterministic-operation) and
  [trust/isolation](workspace-loading.md#trust-and-isolation).
- [Architecture overview](architecture/overview.md): components, dependencies and execution flows.
- [Shared contracts](shared-contracts.md): diagnostics, coverage, cancellation and deterministic identities.
- [Repository model](repository-model.md): ownership and stable project/module identities.
- [Dependency graph](dependency-graph.md): extraction, evidence and traversal.
- [Architecture decisions](architecture/decisions/0001-component-boundaries-and-analysis-engines.md): design rationale and historical changes.

[AGENT.md](../AGENT.md) records coding conventions. See the
[roadmap](https://github.com/kbourzayq/arch-guard/issues/44) for planned work and
[release notes](../CHANGELOG.md) for version history.
