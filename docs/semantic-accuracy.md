# Semantic accuracy and engine parity

Architecture Guardian validates architecture decisions against labeled C# fixtures.
The solution engine loads an explicitly
restored MSBuild context, extracts a graph and runs policy-selected Core rules.
The compiler engine runs in the host's current compilation using an explicit XML
ownership snapshot. It implements AG001, AG002, AG004, AG005 and AG006; AG003 needs
the solution graph and is unavailable in the compiler engine.

## Per-rule precision and limits

| Rule | Solution engine | Compiler engine | Precision and known limits |
| --- | --- | --- | --- |
| AG001 | Evaluated project/assembly references and semantic evidence; one finding per forbidden project pair | Bound explicit symbol uses; one diagnostic per use | Exact, directional module/contract permissions. External assemblies have no module ownership. Compiler requires a snapshot entry for repository targets and misses unused references. |
| AG002 | Evaluated references and semantic evidence; one finding per forbidden project pair | Bound explicit symbol uses | Exact directional layer matrix; same-layer access passes. Module permission does not grant layer permission. Compiler has the same ownership and unused-reference limits as AG001. |
| AG003 | Cyclic module components from direct observed evidence; one representative shortest cycle per component | Unavailable | Static graph only, including allowed contract dependencies. Does not enumerate every possible cycle or infer runtime dependencies. |
| AG004 | Semantic evidence grouped by source project, target project and symbol | Bound explicit symbol uses | Another module's implementation remains forbidden even if C# public or AG001 grants whole-module permission. Whole-project contracts and same-module targets pass. Unused references alone do not violate AG004. Compiler ownership gaps and implicit operations limit coverage. |
| AG005 | Semantic evidence grouped by source project, symbol and module/layer restriction | Bound explicit uses, including external/framework and same-project symbols | Assembly simple names match exactly; namespaces match exact names and descendants at segment boundaries. Text, comments and unused imports do not establish dependencies. Assembly names identify assemblies, not packages; no reflection or runtime discovery. Both module and layer restrictions retain separate findings. |
| AG006 | Semantic evidence grouped by source project, symbol and layer restriction | Bound explicit uses, including external/framework and same-project symbols | Explicit source-layer technology bans; no built-in framework list or project-name inference. Namespace exceptions apply only to AG006, including its assembly bans. AG002/AG005 remain independent. Same static-binding and implicit-operation limits as AG005. |

The compiler checks explicit simple names, generic names and predefined type keywords.
The solution extractor additionally follows inferred types, overloaded operators,
conversions and foreach/await pattern bindings. Neither engine resolves reflection,
runtime dependency injection or concrete virtual dispatch targets. See the
[compiler semantic surface](compiler-analyzer.md#reporting-and-scope) and
[solution semantic surface](dependency-graph.md#semantic-reference-scope).

These are executable regression expectations, not population-level precision or
recall percentages. A passing fixture does not establish soundness for arbitrary
C# programs. IDE UI behavior and compiler hosts outside the documented
[host validation matrix](compiler-analyzer.md#host-compatibility-and-validation)
remain unvalidated.

## Shared cases and intentional differences

`tests/Fixtures/Workspaces/SemanticParity` models Orders consuming Billing contracts
and persistence. The YAML and compiler XML are checked in independently so drift
between the two policy representations changes test results. Source lines labeled
`// case:...` identify positive and negative architectural decisions.

`SemanticAccuracyParityTests` evaluates the same loaded compilations with both
engines across Debug/Release and net10.0/netstandard2.1, loading the explicit
`SemanticParity.slnx` that lists all four projects. For every shared rule it
compares the complete expected set of `(rule, case, restriction)` decisions with
each engine's observed set and with the other engine. Solution observations expand
**all evidence locations**, rather than only each finding's primary location.
Restriction identity retains scope, kind and name, catching missing overlapping
AG005 restrictions as well as unexpected findings on allowed cases.

Cases cover attributes, inheritance/interfaces, constraints, aliases (including
arrays and constructed generics), generic arguments, creation, `typeof`, `nameof`,
casts, patterns, members, extension calls, partial/generated declarations, allowed
contracts, namespace lookalikes, unused imports, literals and AG006 exceptions.
The expected cases change with the active configuration/framework. Inactive branches
contribute no findings. Existing rule and analyzer suites additionally cover
same-module access, whole-module permissions, external/framework symbols, exact-name
matching, severity selection, deterministic identities and cancellation.
Base-class and interface uses have separate labeled lines. The cast case returns
`object` so a forbidden return type cannot mask loss of cast-expression coverage.

Parity compares architectural decisions on the shared surface. It does not require
identical raw diagnostic counts, messages, IDs, symbol display strings or spans:
solution evidence can include constructor/inference observations and wider syntax
spans, while compiler diagnostics locate individual names. Case labels map those
different spans to the same source decision. Unlabeled declarations and
`implicit-operator` are outside the shared comparison. The latter has a separate
assertion requiring all five rules in solution evidence and no compiler finding
on that operation. AG001/AG002 separately assert reference evidence without an
invented source location. Existing AG005/AG006 parity suites assert bound symbols
and generated-source spans directly.

| Context | Explicit outcome |
| --- | --- |
| Multi-target input without `--framework` | Solution `Incomplete`, `framework-required`, gate cannot pass; compiler sees only its current compilation. |
| Selected framework and configuration | Only evaluated references, files and active preprocessor branches participate. Run each supported combination separately; no aggregate coverage claim. |
| Direct multi-target project input | With SDK 10.0.301 / Roslyn 5.0, loading this fixture's `Orders.csproj` directly for `netstandard2.1` produced unmatched project/metadata reference warnings and `Incomplete` coverage. The validated shared matrix uses the explicit `.slnx`; warnings are never suppressed to claim completion. |
| Generated source | Both engines include `.g.cs` partials and actual source-generator trees supplied by Roslyn. Solution locations use portable `generated/...` identities; compiler paths are host supplied. |
| Missing references / unresolved types | Solution remains `Incomplete`, retains bound findings and issues, and fails the gate. Compiler retains host compilation errors, reports still-bound violations and does not invent a dependency for unresolved names. Empty architecture diagnostics cannot establish coverage. |
| Implicit overloaded operator | Solution observes the selected operator and reports applicable rules. The compiler's explicit-name callbacks do not report the implicit operator itself. |
| Unused forbidden project reference | AG001/AG002 solution evidence includes it; compiler reports no dependency without a bound use. AG004–AG006 require semantic uses in both engines. |

## Reproduce and extend

Use the SDK pinned by `global.json` and the committed NuGet lock files:

```sh
dotnet restore ArchitectureGuardian.slnx --locked-mode
dotnet build ArchitectureGuardian.slnx --configuration Release --no-restore
dotnet test tests/ArchitectureGuardian.Tests/ArchitectureGuardian.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~SemanticAccuracyParityTests
```

The test fixture copies inputs to a fresh temporary directory, restores both target
frameworks, stages source generators and loads projects with explicit configuration
and framework. No fixture package additions or network calls occur during analysis;
restore may use the configured package feeds. Shared rules use policy-relative
source paths; normalized decisions ignore checkout directories and ordering.

For AG001/AG002/AG004, reproduce a decision with the labeled project pair and both
ownership snapshots. For AG005, also retain each source module/layer restriction;
for AG006, retain the source layer and allowed namespace exceptions. For AG003,
retain the complete selected solution graph and run the module-cycle regression
suite; a single compiler snapshot cannot reproduce a solution cycle. In every case
record SDK, framework, configuration and generated inputs. Changing any of those
changes the evaluated context.

When extending coverage, add a labeled source case and its independently specified
expected rules/restrictions. Add explicit assertions for intentional engine
differences rather than deleting a failing case or weakening coverage status.
Run the full solution checks before publishing; CI repeats them on Linux, Windows
and macOS and separately validates packaged compiler hosts.
