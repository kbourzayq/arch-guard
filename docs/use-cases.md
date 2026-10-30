# Examples and use cases

Start with a runnable example, then adapt its policy to your projects. The downloads
contain small, package-free .NET 10 solutions, complete YAML, build configuration
and source. Install the [CLI](install.md#install-the-cli) and a .NET 10 SDK; no
Architecture Guardian source build is required.

| Sample | Download | What it demonstrates |
| --- | --- | --- |
| Clean Architecture | [Download ZIP](https://kbourzayq.github.io/arch-guard/examples/clean-architecture.zip) | Domain ports, inward layer directions, forbidden Application → Infrastructure access |
| Modular monolith | [Download ZIP](https://kbourzayq.github.io/arch-guard/examples/modular-monolith.zip) | Orders/Billing contracts, a forbidden module dependency and a logical module cycle |

Review the files before restoring/scanning: `--trusted` authorizes their MSBuild
logic. Extract the archive and run commands inside its top-level sample directory.
Named configurations enable alternative source/references under the same policy.

## Clean Architecture

In the extracted `CleanArchitecture` directory:

```sh
dotnet restore CleanArchitecture.slnx -p:Configuration=Compliant
archguard inspect CleanArchitecture.slnx --policy architecture.guardian.yaml --configuration Compliant --trusted
archguard scan CleanArchitecture.slnx --policy architecture.guardian.yaml --configuration Compliant --trusted
```

Expect `Analysis completed: 0 diagnostic(s). Gate passed.` and exit `0`.
The policy assigns all four projects to Shop. Application and Infrastructure may
depend on Domain; Host may compose Application, Infrastructure and Domain. Domain
has no cross-layer permission. The sample selects AG001–AG003 as errors.

Now enable an Application use that bypasses the Domain port:

```sh
dotnet restore CleanArchitecture.slnx -p:Configuration=AG002
archguard scan CleanArchitecture.slnx --policy architecture.guardian.yaml --configuration AG002 --trusted
```

The code compiles, but AG002 rejects Application → Infrastructure. Expect one
finding, source evidence, `Gate failed` and exit `1`. In
`Shop.Application/Checkout.cs`, `BypassPort` constructs Infrastructure's
`OrderStore` directly. The ordinary `Submit` method depends on `Domain.IOrderStore`.
Use that port and keep implementation selection in Host when repairing the edge.

For your application, map its real projects and allowed directions. Layer names
alone do not impose a universal Clean Architecture layout. See
[modules and layers](concepts/modules-and-layers.md) and [AG002](rule-catalog.md#ag002-layer-dependencies).

## Modular-monolith boundaries

In the extracted `ModularMonolith` directory:

```sh
dotnet restore ModularMonolith.slnx -p:Configuration=Compliant
archguard scan ModularMonolith.slnx --policy architecture.guardian.yaml --configuration Compliant --trusted
```

Expect exit `0`. Orders consumes Billing's declared `api` contract rather than its
application implementation. The two modules have independent contract projects.

Select a context that consumes implementation instead:

```sh
dotnet restore ModularMonolith.slnx -p:Configuration=AG001
archguard scan ModularMonolith.slnx --policy architecture.guardian.yaml --configuration AG001 --trusted
```

Expect one AG001 finding and exit `1`. This sample selects AG001–AG003. In your
repository, add AG004 when cross-module semantic access must remain limited to
contract projects even with a broader AG001 module permission. A C# public type
in another module's implementation is not automatically an architectural API.

### Detect a cycle through approved contracts

```sh
dotnet restore ModularMonolith.slnx -p:Configuration=AG003
archguard scan ModularMonolith.slnx --policy architecture.guardian.yaml --configuration AG003 --trusted
```

Expect one AG003 finding and exit `1`. Both cross-module edges have contract
permissions and the projects build without a reference cycle. The logical module
graph still contains Billing → Orders → Billing. Remove or invert one business
dependency, or introduce an independent shared contract module when appropriate.
Adding permissions does not remove the cycle.

## Keep technology out of Domain code

Use AG006 on an existing Domain layer to ban explicit technology dependencies,
with narrow namespace exceptions. This fragment belongs inside that layer:

```yaml
domainInfrastructure:
  forbiddenNamespaces: [System.Data]
  allowedNamespaces: [System.Data.SqlTypes]
```

Add `AG006: error` to the complete policy's `rules`. Bound uses of `System.Data`
outside the allowed subtree violate AG006; unused imports alone do not. There is
no built-in infrastructure blacklist: choose restrictions for your intended design.

Use AG005 for independent module/layer restrictions on exact assembly names and
namespace subtrees. AG006 exceptions do not override AG005 or AG002. See the
[YAML walkthrough](yaml-guide.md#5-restrict-technology-dependencies),
[AG005](rule-catalog.md#ag005-forbidden-references) and [AG006](rule-catalog.md#ag006-domain-infrastructure).

## Adopt enforcement in an existing codebase

Run a full scan and review its findings. If some debt is deliberately accepted,
write a baseline only after completed analysis:

```sh
archguard baseline update MyApp.slnx --policy architecture.guardian.yaml --baseline architecture.baseline.json --configuration Release --trusted
archguard scan MyApp.slnx --policy architecture.guardian.yaml --baseline architecture.baseline.json --configuration Release --trusted
```

Use your restored input and commit the reviewed baseline. Later scans classify
existing, new and resolved findings and gate only new ones. Keep full scans for
release approval. Never refresh baselines automatically in CI. See [baselines](baselines.md).

For a PR-relative gate, prepare both trees' SDKs/package caches and use
`--base <explicit-commit>` instead. Comparison evaluates complete contexts,
including policy changes and dependencies outside changed files. It does not scan
only edited lines. See [Git analysis](changes.md) and [CI integration](ci.md).

## Architecture-aware AI agents

Configure [MCP](mcp.md) or give an agent access to the installed CLI. The useful loop
is: retrieve policy and resolved ownership, implement the change, analyze, explain
current findings, correct dependencies, and analyze again.

MCP `list_rules` reads policy metadata; `get_architecture` resolves ownership in a
trusted prepared context. Validate with `analyze_solution` and use a fresh diagnostic
ID with `explain_violation`. Require completed coverage and a passed gate alongside
normal build/tests and review. See the [agent workflow](agent-workflow.md).

## Understand incomplete results

Both downloads include an intentionally broken `Incomplete` configuration. Restore
`Compliant` first, then scan with `--configuration Incomplete`. Expect exit `2`,
available findings and operational issues, with no passing gate. This demonstrates
incomplete coverage; it is not a code pattern to copy.

Distinguish boundary violations from unavailable analysis. Use
[diagnostics and outcomes](concepts/diagnostics.md) and [troubleshooting](troubleshooting.md)
to decide the next action.
