# Architecture Guardian for .NET

**Keep architectural boundaries enforceable as your .NET codebase grows.**

Architecture Guardian checks C# solutions against architecture policy stored in
your repository. Define modules, layers and public contracts in YAML, then find
forbidden dependencies with source evidence and guidance for fixing them. Run
checks from the command line, expose architecture context to coding agents through
MCP, or add compiler diagnostics to participating projects.

[Get started](docs/getting-started.md) · [Installation](docs/installation.md) ·
[Documentation](docs/README.md) · [Contributing](CONTRIBUTING.md)

## What you can check

| Rule | What it enforces |
| --- | --- |
| AG001 — Module dependencies | Cross-module references follow explicit module and public-contract permissions. |
| AG002 — Layer dependencies | Dependencies follow your configured layer directions. |
| AG003 — Module cycles | Logical modules do not form dependency cycles, including cycles through contracts. |
| AG004 — Implementation boundaries | Another module's implementation stays private to that module, even when its C# types are public. |
| AG005 — Forbidden references | Modules and layers avoid configured assemblies and namespace trees. |
| AG006 — Domain infrastructure | Selected layers avoid configured technology dependencies, with explicit namespace exceptions. |

Findings include the rule, affected module or symbol, source location when available,
supporting evidence, remediation and documentation. The same source snapshot and
configuration produce deterministic diagnostic identities and ordering.

## Project status

Architecture Guardian is in early development. The supported setup today is a
**source installation** with the SDK selected by [global.json](global.json).
CLI and MCP run on .NET 10; development and CI cover Linux, macOS and Windows.
The compiler analyzer targets `netstandard2.0` with a Roslyn 4.8 API floor.

Full-solution scans, YAML policy, six architecture rules and the MCP server are
implemented, including [explicit diagnostic baselines](docs/baselines.md) and new-violation gates.
[Full-context Git change comparison](docs/changes.md) is available through CLI
`scan --base` and MCP `analyze_changes`. [JSON v1 and SARIF 2.1.0 reports](docs/reports.md)
are available through `scan --format json|sarif`. Packaged CLI installation
is planned. [GitHub Actions and Azure Pipelines examples](docs/ci.md) run independent
CLI gates and retain reports on failure; see the [roadmap](https://github.com/kbourzayq/arch-guard/issues/44).
See [known limitations](docs/troubleshooting.md#known-analysis-limitations)
before adopting a scan as a required gate.

## Install and run your first scan

Install the exact **.NET SDK 10.0.402** selected by [global.json](global.json), then run:

```sh
git clone https://github.com/kbourzayq/arch-guard.git
cd arch-guard
dotnet restore ArchitectureGuardian.slnx --locked-mode
dotnet publish src/ArchitectureGuardian.Cli --configuration Release --no-restore --output artifacts/cli
```

Try the included Clean Architecture example from the checkout root:

```sh
dotnet restore tests/Fixtures/ReferenceSolutions/CleanArchitecture/CleanArchitecture.slnx -p:Configuration=Compliant
dotnet artifacts/cli/ArchitectureGuardian.Cli.dll scan tests/Fixtures/ReferenceSolutions/CleanArchitecture/CleanArchitecture.slnx --policy tests/Fixtures/ReferenceSolutions/CleanArchitecture/architecture.guardian.yaml --configuration Compliant --trusted
```

This configuration completes with zero findings, a passing gate and exit code `0`.
The example has no external package dependencies. `--trusted` authorizes its
repository-controlled MSBuild logic; restore and workspace evaluation can execute
build tasks. Scans do not restore inputs automatically.

Next, follow [Check your own repository](docs/getting-started.md#check-your-own-repository)
to create a policy and scan your solution. The [reference examples](docs/m1-demo.md)
also demonstrate forbidden dependencies, module cycles and incomplete analysis.

## Choose an integration

| Integration | Use it for | Setup |
| --- | --- | --- |
| CLI | Local scans, CI gates, architecture inspection and rule explanations | [CLI guide](docs/cli.md) |
| MCP server | Architecture context and solution analysis in an MCP-compatible coding agent | [MCP setup and tool reference](docs/mcp.md) |
| Compiler analyzer | Feedback on bound symbol uses during C# compilation | [Analyzer installation and policy](docs/compiler-analyzer.md) |

The CLI and MCP share the solution analysis engine. The compiler analyzer uses the
host compilation and a separate XML ownership snapshot; it does not check module
cycles or establish whole-solution coverage. See the [engine comparison](docs/semantic-accuracy.md).

For MCP, publish the server and configure its executable and workspace root using
the [portable configuration templates](docs/mcp.md#configure-your-client). All five
tools are read-only: `get_architecture`, `list_rules`, `analyze_solution`, `explain_violation` and
`analyze_changes`. Workspace execution requires operator trust.
Follow the [coding-agent correction workflow](docs/mcp.md#coding-agent-correction-workflow)
to inspect policy, explain a finding, edit through the client and verify a fresh scan.
Agent validation complements the repository's normal CI gate.

## Understand the result

Completion and policy compliance are separate outcomes:

| CLI exit code | Meaning |
| --- | --- |
| `0` | Analysis completed and the configured severity gate passed. |
| `1` | Analysis completed and the gate failed. |
| `2` | The command could not establish reliable completion: invalid input, missing coverage, failure or cancellation. |

Warnings can pass a gate configured to fail only on errors. An incomplete scan
with no findings still returns `2`. In MCP, check `status` and `gateOutcome` before
using diagnostic counts as a compliance signal.

After explicit restore, analysis itself needs no network access. It does not infer
runtime dependencies through reflection or dependency injection.

## Documentation and support

- [Installation](docs/installation.md): requirements, local executables and analyzer packaging.
- [Getting started](docs/getting-started.md): first scan, policy setup and CI use.
- [Policy reference](docs/policy.md) and [rule catalog](docs/rule-catalog.md).
- [Troubleshooting](docs/troubleshooting.md): SDK, restore, coverage and MCP setup issues.
- [Architecture overview](docs/architecture/overview.md): components, APIs and engine boundaries.
- [Report a bug or request a feature](https://github.com/kbourzayq/arch-guard/issues/new).

Include the command, build context, relevant policy and diagnostic output when
reporting a problem. Remove credentials and private source details before sharing.

## Contributing

Contributions are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) for development
setup, validation commands and pull request guidance. Repository coding conventions
live in [AGENT.md](AGENT.md).

## License

Architecture Guardian is available under the [MIT license](LICENSE).
