# Architecture Guardian documentation

Start with [installation](installation.md), then [run your first scan](getting-started.md).
The guides describe the capabilities available in this source checkout; planned
features are identified explicitly.

## Use Architecture Guardian

| Guide | What you will find |
| --- | --- |
| [Installation](installation.md) | SDK requirements, source installation, CLI/MCP executables and local analyzer packages |
| [Getting started](getting-started.md) | A runnable example, a first policy for your repository and CI exit codes |
| [CLI](cli.md) | Commands, options, output and gate behavior |
| [Baselines](baselines.md) | Explicit accepted-debt updates, stable matching and new-violation gates |
| [Change analysis](changes.md) | Full Git comparison, working-tree semantics and new-finding gates |
| [MCP](mcp.md) | Codex, Claude Code and Copilot CLI setup, correction workflow, workspace trust and tool responses |
| [Compiler analyzer](compiler-analyzer.md) | Local NuGet installation, XML policy, severity and host compatibility |
| [Policy reference](policy.md) | YAML v1, project selectors, contracts, permissions and gates |
| [Rule catalog](rule-catalog.md) | AG001–AG006 semantics and evidence |
| [Examples](m1-demo.md) | Clean Architecture and modular-monolith examples with expected outcomes |
| [Troubleshooting](troubleshooting.md) | Installation, incomplete coverage and MCP errors |

## Understand the engines

- [Semantic accuracy and engine parity](semantic-accuracy.md): what each engine checks and its limits.
- [Workspace loading](workspace-loading.md): restore, trust, configurations, frameworks and SDK selection.
- [Architecture overview](architecture/overview.md): responsibilities, dependencies and execution flows.
- [Shared contracts](shared-contracts.md): diagnostics, completion, cancellation and deterministic identities.
- [Repository model](repository-model.md): normalized ownership and stable project/module identities.
- [Dependency graph](dependency-graph.md): extraction, evidence, aggregation and traversal.
- [Architecture decisions](architecture/decisions/0001-component-boundaries-and-analysis-engines.md): design rationale and implementation history.

## Develop and contribute

See [CONTRIBUTING.md](../CONTRIBUTING.md) for setup, tests and pull requests,
[AGENT.md](../AGENT.md) for coding conventions, and the
[roadmap](https://github.com/kbourzayq/arch-guard/issues/44) for planned work.
