# Semantic accuracy and engine parity

Architecture Guardian validates architecture decisions against labeled C# fixtures.
The solution engine loads an explicitly
restored MSBuild context, extracts a graph and runs policy-selected Core rules.
The compiler engine runs in the host's current compilation using the shared YAML policy and evaluated build metadata
(or legacy XML during the transition release). It implements AG001, AG002, AG004, AG005 and AG006; AG003 needs
the solution graph and is unavailable in the compiler engine.

## Per-rule precision and limits

| Rule | Solution engine | Compiler engine | Precision and known limits |
| --- | --- | --- | --- |
| AG001 | Evaluated project/assembly references and semantic evidence; one finding per forbidden project pair | Bound explicit symbol uses; one diagnostic per use | Exact, directional module/contract permissions. External assemblies have no module ownership. Compiler requires project ownership metadata for repository targets and misses unused references. |
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
[solution semantic surface](concepts/dependency-analysis.md#semantic-reference-scope).

These are executable regression expectations, not population-level precision or
recall percentages. A passing fixture does not establish soundness for arbitrary
C# programs. IDE UI behavior and compiler hosts outside the documented
[host validation matrix](compiler-analyzer.md#host-compatibility-and-validation)
remain unvalidated.

## Shared cases and intentional differences

| Context | How to interpret it |
| --- | --- |
| Multi-target input without a selected framework | CLI/MCP remain incomplete; compiler feedback covers only its current compilation |
| Selected configuration/framework | Only that context's references, files, generators and active preprocessor branches participate |
| Generated source | Both engines analyze generated C# supplied by Roslyn; solution locations use logical identities and compiler locations use host paths |
| Missing references or unresolved types | Resolve compiler/workspace issues; still-bound findings may be useful but an empty result does not establish coverage |
| Implicit operators, conversions or pattern methods | Solution analysis can observe bindings beyond the compiler analyzer's explicit-name callbacks |
| Unused forbidden project reference | AG001/AG002 solution evidence can report it; compiler callbacks require a bound use |
| Different diagnostic counts | Compare rule, ownership, restriction and bound use rather than IDs or counts; solution findings aggregate evidence |

If direct multi-target project loading reports unmatched project/metadata reference
warnings, try the explicit solution containing referenced projects, still selecting
the framework and mapping the complete scope. Keep warnings visible and require
completed coverage. See [workspace preparation](workspace-loading.md) and
[troubleshooting](troubleshooting.md#multi-targeting-and-configurations).

## Choose your enforcement checkpoints

Use compiler feedback for early correction of supported explicit uses. Retain
CLI/MCP solution scans for broader semantic evidence, unused reference checks and
AG003 cycles. Cover every context required by CI through separate runs. A successful
compiler build does not establish whole-solution architecture compliance.

Runtime reflection, dependency-injection choices and concrete virtual dispatch
targets remain outside the static model. Follow the [rule catalog](rule-catalog.md)
for individual constraints and [diagnostics](concepts/diagnostics.md) for coverage
and gate meaning.
