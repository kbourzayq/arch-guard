# Architecture Guardian documentation

Define architectural boundaries in YAML and enforce them in your .NET codebase.
These guides explain how to install the product, model your architecture, interpret
findings, and share a policy across your editor, CI pipeline and coding agents.

## Choose your starting point

| I want to… | Start here |
| --- | --- |
| Run my first architecture check | [Install the CLI](install.md#install-the-cli), then follow the [quick start](getting-started.md) |
| Understand the model before writing YAML | [Architecture as code](concepts/architecture-as-code.md), then [modules, layers and contracts](concepts/modules-and-layers.md) |
| Learn the configuration language | [Write a YAML policy](yaml-guide.md), then use the [complete reference](policy.md) |
| Get IDE/build feedback | [Install the Roslyn analyzer](compiler-analyzer.md#install-and-distribute-policy) |
| Check pull requests in CI | [Add an independent quality gate](ci.md) |
| Give an AI agent architecture context | [Configure MCP](mcp.md#configure-your-client), then [use the agent workflow](agent-workflow.md) |
| Evaluate cost and coverage | [Benchmarks](benchmarks.md), [engine differences](semantic-accuracy.md) and [known limitations](troubleshooting.md#known-analysis-limitations) |

## Your first architecture gate

1. **Install and verify the tool.** CLI/MCP need .NET 10 and the SDK required by
   the repository you analyze. Use the released [NuGet packages](install.md).
2. **Choose a complete scope.** Map every loaded project, including references,
   to exactly one module and layer in a [first policy](getting-started.md#2-write-a-policy).
3. **Restore, inspect and scan.** Use the same configuration/framework for
   [preparation and analysis](getting-started.md#3-restore-inspect-and-scan).
4. **Repair a real violation.** Try the [downloadable examples](use-cases.md),
   inspect evidence, correct the dependency and rerun the gate.
5. **Keep the check in your workflow.** Share YAML with the analyzer and add a
   solution scan to CI. Use MCP when an agent should inspect and validate architecture.

The [quick start](getting-started.md) takes you through these steps. The installed
tools and downloadable samples do not require an Architecture Guardian source checkout.

## Core concepts

- [Architecture as code](concepts/architecture-as-code.md): how policy, observed code and enforcement fit together.
- [Modules, layers and contracts](concepts/modules-and-layers.md): ownership, technical direction and intentionally shared APIs.
- [Dependency analysis](concepts/dependency-analysis.md): observed evidence, semantic scope and static-analysis limits.
- [Diagnostics and outcomes](concepts/diagnostics.md): findings, completion, severities and gates.

## Find the task you need

| Use case | Guidance |
| --- | --- |
| Keep Clean Architecture dependencies pointing inward | [Layer boundaries](use-cases.md#clean-architecture), AG002 and explicit Domain restrictions |
| Isolate a modular monolith | [Contracts and cycles](use-cases.md#modular-monolith-boundaries), AG001/AG003/AG004 |
| Keep frameworks out of business logic | [Technology restrictions](use-cases.md#keep-technology-out-of-domain-code), AG005/AG006 and exceptions |
| Adopt enforcement with existing debt | [Baseline adoption](use-cases.md#adopt-enforcement-in-an-existing-codebase) and [Git analysis](changes.md) |
| Make agents respect boundaries | [Inspect, implement, validate and correct](use-cases.md#architecture-aware-ai-agents) |

## Integrations and reference

Use the [CLI](cli.md), [MCP](mcp.md), [analyzer](compiler-analyzer.md) and
[CI](ci.md) guides for integration. Exact contracts are in the [YAML reference](policy.md),
[AG001–AG006 catalog](rule-catalog.md), [JSON/SARIF reports](reports.md),
[baselines](baselines.md), [Git comparison](changes.md), and [compatibility policy](releases.md).
Operational guidance covers [workspace loading](workspace-loading.md),
[performance and telemetry](performance.md) and [troubleshooting](troubleshooting.md).

## Know when a result is usable

| CLI exit | Meaning | Next action |
| --- | --- | --- |
| `0` | Analysis completed and the selected gate passed | Continue with normal build, tests and review |
| `1` | Analysis completed and the gate failed | Correct or explicitly review the architecture finding, then rescan |
| `2` | Invalid input, incomplete analysis, failure or cancellation | Resolve the operational issue and rerun before drawing a compliance conclusion |

For MCP, check both `status` and `gateOutcome`. Compiler feedback covers its host
compilation; retain solution scans for broader context and AG003 cycles. A
new-finding gate may pass while existing errors remain. An empty incomplete result
never establishes a successful gate.

See [release notes](../CHANGELOG.md) and the
[roadmap](https://github.com/kbourzayq/arch-guard/issues/44) for changes and plans.
Report reproducible problems through [GitHub issues](https://github.com/kbourzayq/arch-guard/issues/new),
including tool version, build context, relevant policy and redacted diagnostics.
