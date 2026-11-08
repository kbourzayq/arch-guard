# Architecture discovery and policy proposals

`init --discover` is available in the 0.4.0 CLI. For source execution, follow
[local development](installation.md) and replace `archguard` below with
`dotnet artifacts/cli/ArchitectureGuardian.Cli.dll`. Check `init --help` on your
installed version before using these commands.

Discovery separates verified facts, candidate architectural intentions,
configuration validity and architecture compliance. It requires no approved policy
and never activates a proposal automatically.
The optional [onboarding agent skill](onboarding.md) guides ownership review and
adoption with discovery when installed, or manual proposals with current tools.

## Explore and select a context

From the target repository root, choose an input without authorizing builds:

```sh
archguard init --discover .
```

Multiple top-level `.sln`/`.slnx` files produce a list of choices and exit `2`;
specify one with `--discover <path>`. Without a top-level solution, C# projects are
listed recursively, excluding build outputs, tool indexes and linked directories.
A single choice is selected. Explicit `.sln`, `.slnx` and `.csproj` inputs work too;
project inputs include referenced projects.

Without `--trusted`, only literal XML/solution project paths and literal project
references are read. Conditions, imports, globs and MSBuild expressions are not
expanded. No workspace, generators or restore run. The static inventory is labeled
**unevaluated**, coverage is incomplete, exit is `2`, and only a JSON report is
written. Move that report before rerunning because outputs are never overwritten.

Literal child references can be inactive under the selected configuration/framework.
Outside-root child hints are recorded without traversal; unreadable child XML and
unavailable paths remain `staticInventory.issues`. These hints do not block trusted
evaluation or determine its coverage. Only evaluated projects must satisfy the
verified inventory's root and ownership requirements.

Review and authorize the repository's build logic, restore the intended context,
then explicitly select it:

```sh
dotnet restore Shop.slnx -p:Configuration=Release
archguard init --discover Shop.slnx --configuration Release --trusted
```

Trusted discovery requires `--configuration <name>`. Multi-targeted inputs also
require `--framework <tfm>` and restored assets for that context. Explore each
required configuration/framework separately, retaining reports between runs.
`exploredContexts` records actual evaluated project contexts; `declaredFrameworks`
lists choices including unexplored targets. Missing assets/references, compiler or
generator errors and cancellation retain incomplete, failed or cancelled coverage.
Partial inventory cannot generate YAML.

## Review intentions and evidence

The command creates files in the selected input's directory:

- `architecture.guardian.proposed.yaml`: review-only v1 intentions, written only
  after completed inventory and unambiguous candidate ownership.
- `architecture.guardian.discovery.json`: versioned observations, contexts,
  ownership/style/contract inferences with confidence and evidence, naming
  inconsistencies, unresolved questions and independent candidate compliance.

`--output-directory <existing-directory>` chooses a root above the complete input,
project and source inventory. Exact selectors are relative to this directory.
Existing outputs are protected, including concurrent creations. The command
changes no active policy, source, references or baseline. A concurrent collision
can retain the report while preventing YAML publication; it returns `2`.

Role-bearing names, directory segments and declared namespaces can suggest Domain,
Application, Infrastructure/Persistence, Contracts/Abstractions, Host/Api/Web and
Tests layers. A `.Tests` suffix takes the test role. Module prefixes require project
or namespace evidence. Unrecognized/conflicting ownership leaves actionable
questions and the complete observed inventory, with no YAML. Custom assembly names
are identities, independently of ownership.

Clean Architecture, Modular Monolith and Vertical Slice are evidence-backed style
suggestions, not approval of intent. Unstructured layouts receive a Custom
suggestion and require manual project mappings. Contract candidates require an
explicit Contracts/Abstractions role and public declarations. Multiple observed
consumers can suggest shared abstractions; review the entire API. Schema v1 owns
whole projects: namespace/feature slices and runtime/reflection dependencies remain
explicit limitations.

Layer directions are conventional review intentions: Application → Domain/Contracts,
Infrastructure → Application/Domain/Contracts, Host → those inner layers, and
Tests → production layers. Reverse directions remain denied. Cross-module
permissions start empty, including observed contract accesses. All six rules are
enabled as errors. Domain candidates receive explicit build/compiler namespace
bans and data-access/ORM restrictions; review these technology intentions too.
Observed edges never create permissions. Forbidden edges remain compliance findings.

## Validate and manually adopt

Successful proposal generation returns `0`, including when candidate compliance
fails. Unresolved ownership, noncompleted coverage, invalid inputs and output
failures return `2`. Discovery does not use scan's exit `1` for candidate findings.
Read `compliance` separately from `configurationValidation: "valid"`.

Review the YAML, questions and evidence. Validate ownership/selectors, then evaluate
the candidate with the existing commands in the same context:

```sh
archguard inspect Shop.slnx --policy architecture.guardian.proposed.yaml --configuration Release --trusted
archguard scan Shop.slnx --policy architecture.guardian.proposed.yaml --configuration Release --trusted --format json
```

`inspect` validates configuration and loaded inventory, without evaluating rules.
Completed `scan` returns `0` for a passing gate or `1` for a failing gate. Resolve
findings or deliberately review architecture changes; never add permissions merely
to silence observed problems. Keep the independent [CI gate](ci.md).

After review and validation, manually copy the proposal to
`architecture.guardian.yaml` when that file does not exist, or deliberately
reconcile it with the existing approved policy. Discovery has no adoption or force
option. Re-run inspect/scan against the adopted path in the same contexts.

## Report contract and determinism

[Discovery report v1](schema/discovery.v1.schema.json) defines the separate JSON
envelope (`schemaVersion: 1`, `kind: "architecture-discovery"`). `observations`
contains neutral project/assembly/namespace/declaration inventory and unaggregated
semantic evidence. Direct project-reference evidence stays distinct from resolved
assembly references, which may include transitive compiler references. Generated
file-local types retain stable generator/hint identities, without physical paths.
`candidateOwnership` and `inferences` remain separate from those facts.
`proposedPolicy`, when present, follows the existing
[policy v1 schema](schema/architecture.guardian.v1.schema.json); there is no new
policy format. `compliance` holds canonical rule findings against that candidate.

Identical completed contexts yield byte-identical YAML/JSON independently of
enumeration and checkout roots. Outputs use UTF-8 without BOM, LF newlines,
ordinal identities/order and no timestamps or absolute checkout root. Operational
failure messages may contain environment paths: these are non-semantic diagnostic
metadata for unavailable contexts. Coverage verifies only the explored snapshot
and context. Neutral contracts and the reusable service support other onboarding
hosts without depending on agents or an LLM.
