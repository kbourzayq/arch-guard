# Architecture Guardian documentation

Define architectural boundaries in YAML, inspect real C# dependencies, and keep
the same policy in your editor, CI pipeline and coding-agent workflow. These guides
take you from installing your first tool to understanding individual rules,
operational coverage and the implementation contracts behind the tools.

## Choose your starting point

You can use Architecture Guardian without building this repository. Install the
integration that fits your workflow, then share one repository-owned YAML policy
across the tools you use.

| I want to… | Start here | Keep in mind |
| --- | --- | --- |
| Scan a .NET solution locally | [Install the CLI](../README.md#install-the-cli), then [write your first policy](getting-started.md#check-your-own-repository) | Restore the same configuration/framework you will scan |
| Get feedback while editing or building C# | [Install the compiler analyzer](compiler-analyzer.md#install-and-distribute-policy) | A compiler invocation checks its own context; AG003 module cycles require a solution scan |
| Give a coding agent architecture context | [Connect the MCP server](mcp.md#configure-your-client), then [add the agent workflow](agent-workflow.md) | Configure an absolute workspace root and authorize build logic before workspace analysis |
| Prevent new violations in pull requests | [Add an independent CI gate](ci.md#gate-with-the-installed-cli) | Fail the pipeline on both policy violations and operational failures |
| Contribute to the tools | [Set up a source checkout](installation.md), then [read the contribution guide](../CONTRIBUTING.md) | Only source development requires this repository's exact pinned SDK |

## Your first architecture gate

Follow this path to reach a result you can interpret, with working examples at each step:

1. **Install and verify the command.** Check [runtime and SDK requirements](../README.md#requirements)
   and run the [CLI installation checks](../README.md#install-the-cli).
2. **Map the complete scope.** The [quick start](getting-started.md#1-define-the-analysis-scope)
   explains solution/project inputs and why every loaded project needs exactly one
   module and layer, including referenced projects and tests.
3. **Declare the allowed direction.** Adapt the [first YAML policy](getting-started.md#2-write-a-policy).
   Use the [policy reference](policy.md) for globs, contracts, rule selection and severity.
4. **Restore, inspect and scan.** Follow the [same-context commands](getting-started.md#3-restore-inspect-and-scan).
   Inspect ownership before interpreting findings.
5. **Read and repair a violation.** Run the [included passing and violating examples](getting-started.md#run-the-included-example),
   then use the [rule catalog](rule-catalog.md) to understand evidence and corrective options.
6. **Keep the gate in your workflow.** Add [compiler feedback](compiler-analyzer.md)
   and an [independent CI scan](ci.md); optionally give [agents the same context](agent-workflow.md).

### Know when a result is usable

| CLI exit | Meaning | Next action |
| --- | --- | --- |
| `0` | Analysis completed and the configured severity gate passed | Continue with the normal build, tests and review |
| `1` | Analysis completed and the gate failed | Inspect evidence, correct the boundary violation, then rescan |
| `2` | Input, coverage, failure or cancellation left compliance unresolved | Resolve the operational problem and rerun; an empty result is not a pass |

For MCP, check both `status` and `gateOutcome`; see the
[response contracts](mcp.md#tool-requests-and-response-schemas-version-1).
A new-finding gate may pass with accepted debt: [baselines](baselines.md) and
[Git comparisons](changes.md) complement a full scan rather than replace it.

## Find the task you need

| Task | Focused guidance |
| --- | --- |
| Model a modular monolith or Clean Architecture boundary | [Ownership](policy.md#module-layer-and-contract-resolution), [dependency permissions](policy.md#dependency-permissions), [reference examples](m1-demo.md) |
| Decide which rules to enable | [AG001–AG006 catalog](rule-catalog.md), [rule selection and gates](policy.md#rule-selection-severity-and-gates), [engine precision and limits](semantic-accuracy.md) |
| Understand a source location or remediation | [CLI diagnostics](cli.md#scan-a-solution-or-project), [diagnostic contracts](shared-contracts.md), [MCP explanation](mcp.md#explain_violation) |
| Adopt enforcement in a repository with existing debt | [Reviewed baselines](baselines.md), [Git change analysis](changes.md), [CI debt-policy choices](ci.md#choose-the-accepted-debt-policy-explicitly) |
| Consume results in CI or code scanning | [JSON/SARIF reports](reports.md), [gate outcomes and artifacts](ci.md#outcomes-and-retained-artifacts) |
| Resolve missing assets, SDKs, generators or multi-targeting | [Workspace troubleshooting](troubleshooting.md#restore-and-workspace-problems), [workspace loading](workspace-loading.md) |
| Fix installation or agent connection problems | [Command startup](troubleshooting.md#installation-and-command-startup), [MCP connection and paths](troubleshooting.md#mcp-connection-and-path-problems) |
| Evaluate runtime cost or offline behavior | [Performance budgets](performance.md), [offline operation](workspace-loading.md#offline-and-deterministic-operation) |

For support boundaries, read the [known limitations](troubleshooting.md#known-analysis-limitations)
and [per-rule precision matrix](semantic-accuracy.md#per-rule-precision-and-limits).
Neither a valid policy nor a successful compiler build proves whole-solution architecture compliance.

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
| [Manual validation](manual-validation.md) | Run this checkout with its current analyzer and six-rule CLI gate |
| [Compiler analyzer](compiler-analyzer.md) | Shared YAML, legacy XML migration, severities and host compatibility |
| [Policy](policy.md) | YAML v1, ownership, contracts, layers, rule selection and editor schema |
| [Rule catalog](rule-catalog.md) | AG001–AG006 examples, evidence, remediation and limits |
| [Baselines](baselines.md) | Reviewed accepted debt, stable matching and new-finding gates |
| [Change analysis](changes.md) | Full Git comparison and working-tree semantics |
| [Reports](reports.md) | JSON v1, SARIF 2.1.0, file output and validation |
| [CI quality gates](ci.md) | Independent installed-CLI gate and complete source-based pipeline examples |
| [Performance and telemetry](performance.md) | Reproduce measured budgets and collect opt-in local phase/rule metrics |
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

## Maintain the documentation

The [documentation website](https://kbourzayq.github.io/arch-guard/) adds full-text
search and sidebar navigation to these guides. This folder remains the
authoritative source. See [website maintenance](../website/README.md) for local
preview, source mappings, validation and GitHub Pages setup.
