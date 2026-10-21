# Normalized repository model

The repository model provides immutable architecture data in
`ArchitectureGuardian.Core.Repositories`. `RepositoryModelBuilder` in
`ArchitectureGuardian.Analysis.Repositories` converts an already loaded
workspace into neutral data and applies the [v1 configuration policy](policy.md).
The model contains no Roslyn/MSBuild objects and remains usable after workspace disposal.

## Contents and ownership

| Contract | Contents and identity scope |
| --- | --- |
| `RepositoryModel` | Solution or project input, projects, policy, resolved ownership, modules and layers |
| `RepositoryProject` | Policy-relative project path, workspace display name and one evaluated output assembly |
| `RepositoryAssembly` | Evaluated assembly name and source namespaces, scoped to its project |
| `RepositoryNamespace` | Fully qualified namespace name and explicit source types, scoped to its project; empty name is the global namespace |
| `RepositoryType` | Qualified metadata name, declaration kind, effective public visibility and optional file-local discriminator, scoped to its project |
| `RepositoryGeneratedFile` | Logical generated-file identity from generator assembly simple name, full generator type name and source hint |
| `RepositoryModule` | Policy module identity, member projects and named public contracts |
| `RepositoryContract` | `MODULE/CONTRACT` identity and the whole-project public surface selected by policy |
| `RepositoryLayer` | Policy layer identity and projects across modules |

Modules and layers are independent views over the same project instances. A Billing
module can span Domain, Application, Contracts and Infrastructure projects. Layers
can contain projects from multiple modules. Namespaces and assemblies do not infer
ownership: an assembly named `Company.Billing.Messages` can belong to the
`Billing.Contracts.csproj` project and the `Billing/api` contract.

`RepositoryModel.ResolvedPolicy.Projects` gives every project's single module,
single layer and optional contract membership. Construction delegates to
`ResolvedPolicy`: unmapped projects, overlapping module/layer/contract ownership,
duplicate projects, empty inventories, unused selectors and contracts outside their
module are errors. There is no default or ignored-project fallback.

Contracts select entire projects in v1. `IsPublic` means the type and every containing
type are public; it does not grant dependency permission or enforce symbol access.
`AllowsModuleDependency` and `AllowsLayerDependency` retain the policy's separate,
direct and directional permission checks. The model does not extract edges or report
violations; [dependency extraction](dependency-graph.md) consumes this normalization,
and [AG001](rule-catalog.md#ag001-module-dependencies) evaluates its evidence against
the resolved module permissions. [AG002](rule-catalog.md#ag002-layer-dependencies)
evaluates layer permissions independently. Module-cycle evaluation uses the dependency graph.

## Identities and reproducibility

The caller supplies the directory containing the policy. Input and project identities
are paths relative to that directory, with `/` separators and ordinal, case-sensitive
comparison. Rooted paths, `..`, dot segments and noncanonical paths are rejected.
Place the policy above the solution input, every loaded project (including references
outside the solution) and any authored file-local source files. No repository-root guessing
or filesystem-based case folding changes matching behavior.

`RepositoryModel.Id` is the relative solution or project input path, and
`RepositoryProject.Id` is its relative project path. Assembly, namespace and type IDs
are lowercase SHA-256 hashes of length-prefixed scope fields:

| Entity | Identity fields, in order |
| --- | --- |
| Assembly | `assembly`, project path, evaluated assembly name |
| Namespace | `namespace`, project path, full namespace name |
| Generated file | `generated-file`, generator assembly simple name, full generator type name, slash-normalized hint name |
| Type | `type`, project path, qualified metadata name, file-local discriminator or null |

Type names include generic arity (for example ``Billing.Invoice`1``) and `+` for
nesting. Partial declarations become one type; nested types appear
in their namespace's type collection. Namespace components use symbol names without
C# escaping: `namespace @class` produces `class.Test`, not `@class.Test`.

Authored file-local types and their nested types use the policy-relative
`DeclaringFile` path as their discriminator. Generated file-local types instead carry
`GeneratedFile`; their discriminator is `generated:` followed by that logical file's
ID. A type cannot carry both. Generator assembly paths and versions, intermediate
directories and Debug/Release configuration do not enter this identity. Different
generators or hints can declare otherwise identical file-local names without colliding.
Generated output can be outside the policy directory without reducing coverage.

Roslyn 5.9 exposes the source hint publicly and keeps the document's generator
identity internal. The adapter associates each generated document's logical
assembly/type/hint suffix with a registered generator using the public underlying
generator-type API. It discards the physical output prefix; ambiguous or unavailable
associations fail explicitly. The builder also excludes compiler-generated file-local
type-name prefixes, which can depend on absolute paths. Two ordinary types with the
same name in different projects remain distinct.

Only the current assembly's source namespace tree is traversed, including explicit
source-generator declarations. Referenced framework/package/project types are not
copied into the referencing assembly. Implicit compiler helper types are excluded.
The model reflects the loaded configuration and framework, including active
conditional compilation, as described in [workspace loading](workspace-loading.md).

All collections are copied, read-only and ordered by ordinal identity. Reversing an
inventory or relocating an unchanged checkout preserves identities and ordering.
Absolute paths, workspace GUIDs, assembly versions, output directories, culture and
enumeration order do not enter identities. Changing logical paths or names can change
identities; these are snapshot identities, not baseline matching guarantees.

## Reusable API and coverage

After restoring and trusting the input, a host referencing Configuration and Analysis
can build the model:

```csharp
using ArchitectureGuardian.Analysis.Repositories;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;

var policy = await new YamlPolicyLoader().LoadAsync(policyPath, cancellationToken);
using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(
    new WorkspaceLoadRequest(inputPath, isTrusted: true), cancellationToken);
var repository = await new RepositoryModelBuilder().BuildAsync(
    workspace, Path.GetDirectoryName(Path.GetFullPath(policyPath))!, policy,
    cancellationToken);

if (repository.Status != AnalysisStatus.Completed || repository.Model is null)
{
    // Surface repository.Issues and the coverage status to the caller.
    return;
}

var model = repository.Model;
// The host can now supply this neutral snapshot to future repository rules.
```

`RepositoryBuildResult` carries `Model`, `Status` and canonically ordered `Issues`.
Completion requires a model and no operational issues. A completed model build is
not an architecture scan or a quality-gate result. Hosts must carry build coverage
into any later evaluation rather than accept findings from incomplete input as clean.

| Condition | Result |
| --- | --- |
| Complete workspace and successful normalization/resolution | `Completed` with model |
| Incomplete workspace with enough declarations to resolve policy | `Incomplete` with available model and original loading issues |
| Invalid paths, missing/ambiguous ownership or unused selectors | `Incomplete`, null model, `repository-mapping-invalid` issue |
| No workspace | `Incomplete`, null model, original issues plus `repository-workspace-unavailable` |
| Unsupported project or unavailable compilation | Operational issues prevent completion; model exists only if the remaining inventory resolves |
| Unexpected normalization exception | `Failed`, null model, `repository-normalization-failed` |
| Requested cancellation or cancelled loading | `Cancelled`, null model, available loading issues retained |

The builder performs no restore, workspace opening or policy-file loading. It forwards
cancellation while obtaining compilations and checks it during symbol traversal.
CLI `load` validates workspace context only. Both
[`inspect` and `scan`](cli.md) compose normalization; MCP uses the same services.

## Reference fixture

The executable [modular-monolith fixture](../tests/Fixtures/Workspaces/ModularMonolith/ModularMonolith.slnx)
and its [policy](../tests/Fixtures/Workspaces/ModularMonolith/architecture.guardian.yaml)
contain Billing's four projects plus Orders.Application. Orders references
Billing.Contracts and calls `IInvoice.Total`. Policy permits `contract:Billing/api`
and the Contracts layer, while denying Orders access to Billing.Infrastructure.
Integration tests verify this real reference, normalized ownership, source types,
workspace error propagation and all identities after copying/restoring the fixture
under a different absolute directory. Relocated restores use the same serialized
restore helper as other workspace fixtures, avoiding concurrent NuGet writes through
macOS temporary-directory aliases. Generator tests cover matching file-local names
from distinct generators and hints, Debug/Release identity parity and external
intermediate directories. Keyword-namespace tests resolve normalized names back
through the compilation. Core tests cover immutability, scoped identities and strict
resolution failures.
