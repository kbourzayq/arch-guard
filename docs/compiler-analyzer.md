# Compiler analyzer

Add architecture feedback to C# compilation with `ArchitectureGuardian.Analyzers`.
It targets `netstandard2.0`, uses the host's current compilation and reads an explicit
XML ownership snapshot supplied as an AdditionalFile.

The analyzer checks AG001, AG002, AG004, AG005 and AG006. It shares their permission
semantics with the solution engine, but reports bound type/member uses at source
locations rather than grouping evidence by project pair. Counts and diagnostic
identities are not interchangeable across engines. It does not load YAML, restore
projects, open a workspace or establish whole-solution cycle coverage.

Keep CLI or MCP solution scans as the full architecture gate. See
[engine scope and limits](semantic-accuracy.md) before choosing compiler-only checks.

## Install and distribute policy

### 1. Build a local package

From the Architecture Guardian checkout, after the locked restore in
[installation](installation.md):

```sh
dotnet pack src/ArchitectureGuardian.Analyzers --configuration Release --no-restore --output artifacts/analyzers
```

The current version produces `ArchitectureGuardian.Analyzers.0.1.0.nupkg`.
These instructions use a local package source and do not assume nuget.org publication.
The complete [distribution build](releases.md#build-and-verify-packages) produces
the same analyzer package alongside the CLI/MCP tools in `artifacts/packages`.
All three packages share the root version and [compatibility policy](releases.md).

### 2. Reference the analyzer and policy

In the consuming repository, add the following to a root `Directory.Build.props`
(or merge the ItemGroup into your existing file). The package applies to all
participating projects; every such assembly needs a mapping in the snapshot.

```xml
<Project>
  <ItemGroup>
    <PackageReference Include="ArchitectureGuardian.Analyzers" Version="0.1.0" PrivateAssets="all" />
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)architecture.guardian.compiler.xml" />
  </ItemGroup>
</Project>
```

Create `architecture.guardian.compiler.xml` alongside that props file using the
[snapshot format below](#compiler-policy-snapshot). Then restore the consuming
solution with the local source and your normal package feeds. Replace both
`MyApp.slnx` and the absolute `<LOCAL_PACKAGE_DIRECTORY>` placeholder:

```sh
dotnet restore MyApp.slnx --source "<LOCAL_PACKAGE_DIRECTORY>" --source https://api.nuget.org/v3/index.json
dotnet build MyApp.slnx --no-restore
```

`<LOCAL_PACKAGE_DIRECTORY>` is the directory containing the `.nupkg`, normally
`artifacts/analyzers` in your Architecture Guardian checkout. `--source` overrides
configured feeds for this restore; also supply any private feeds required by your
solution, or add the local source to your existing NuGet configuration instead.
Do not commit a developer's absolute package-directory path to shared configuration.
Set [reporting severity](#reporting-and-scope) in the consuming repository's
`.editorconfig`; CLI YAML severity settings do not configure compiler diagnostics.

The package contains only `analyzers/dotnet/cs/ArchitectureGuardian.Analyzers.dll`;
it has no runtime library or NuGet dependencies. Roslyn assemblies come from the host.
`PrivateAssets="all"` prevents unintended distribution to consumers of your own package.

## Compiler policy snapshot

Commit the compiler snapshot alongside the authoritative `architecture.guardian.yaml`.
Maintain it explicitly from the YAML's resolved project inventory:
each participating project's evaluated `AssemblyName` maps to its module, layer and
optional whole-project public contract. Do this outside compiler callbacks. There is
no automatic YAML exporter yet. Include all in-scope assemblies, including project
references; namespace prefixes and filenames do not establish ownership. Update both
policies when permissions, assembly names or ownership change, and retain CLI `scan`
in CI as the authoritative solution-wide gate. The snapshot is also distributable
through an internal policy package's `buildTransitive` props, with an explicit
AdditionalFiles item pointing at its packaged snapshot.

```xml
<ArchitectureGuardian version="1">
  <Module id="Orders" allows="contract:Billing/api" forbiddenAssemblies="Legacy.Data" forbiddenNamespaces="System.Net" />
  <Module id="Billing" />
  <Layer id="Application" allows="Contracts" />
  <Layer id="Contracts" />
  <Layer id="Infrastructure" allows="Contracts" />
  <Assembly name="Orders.Application" module="Orders" layer="Application" />
  <Assembly name="Billing.Contracts" module="Billing" layer="Contracts" contract="api" />
  <Assembly name="Billing.Infrastructure" module="Billing" layer="Infrastructure" />
</ArchitectureGuardian>
```

Modules and layers also accept optional `forbiddenAssemblies` and `forbiddenNamespaces`
attributes: whitespace-separated unique names, equivalent to the YAML lists on the
same source module/layer. An absent or empty attribute means no restrictions.
Use exact case-sensitive ASCII assembly simple names (letters, digits, `.`, `_`, `-`)
and dot-separated ASCII namespace identifiers (letter or `_` first, then letters,
digits or `_`). Namespace restrictions include descendants, with segment boundaries;
`System.NetLike` does not match `System.Net`. Wildcards, full assembly identities,
`global::` and escaped names are unsupported. Keep these restrictions synchronized
with the authoritative [YAML policy](policy.md#forbidden-references-ag005).

For AG006, only `Layer` elements accept `infrastructureAssemblies`,
`infrastructureNamespaces` and `allowedInfrastructureNamespaces`. These are
whitespace-separated unique names corresponding respectively to YAML
`domainInfrastructure.forbiddenAssemblies`, `.forbiddenNamespaces` and
`.allowedNamespaces` on the same layer. Absent/empty attributes ban nothing; names
use the same validation and namespace descendant matching as AG005. For example:

```xml
<Layer id="Domain" infrastructureAssemblies="Legacy.Persistence"
       infrastructureNamespaces="Microsoft.EntityFrameworkCore System.Data"
       allowedInfrastructureNamespaces="System.Data.SqlTypes" />
```

The source assembly's configured layer decides scope, regardless of its name. The
exception applies to target namespace subtrees for AG006 only, including assembly
bans, across all target assemblies. Keep it narrow and synchronize it with YAML.

Version 1 accepts only the shown root, child elements and attributes. IDs and assembly
names are nonempty, case-sensitive tokens containing letters, digits, `.`, `_` or `-`.
Module permissions are whitespace-separated `module:ID` or `contract:MODULE/CONTRACT`
tokens; layer permissions are whitespace-separated layer IDs. Duplicate declarations,
duplicate permissions, unknown attributes/elements, missing ownership, dangling
permissions and DTDs are rejected. Contract IDs belong to their declared module;
multiple assemblies can share a module or a contract. Assembly names must be unique
within the snapshot; repositories with ambiguous simple names require a different
snapshot per build context. Select the appropriate snapshot for each target framework
when evaluated ownership or assembly names differ.

## Reporting and scope

- AG001 denies cross-module semantic uses unless the source module permits the exact
  target module or its exact public contract. Same-module uses are allowed.
- AG002 independently checks the configured directional layer matrix; same-layer uses
  are allowed. Permissions are direct and are never inferred transitively.
- AG004 rejects semantic uses of implementation assemblies in another module,
  including C# `public` types such as `CatalogDbContext`. A declared whole-project
  contract (`contract` on the assembly snapshot) or same-module ownership passes.
  A `module:ID` permission does not exempt implementation access. AG001 still decides
  whether a contract dependency is permitted, and AG002 still checks layers.
- AG005 checks bound type/member uses against both the source module's and source
  layer's explicit restrictions. It includes framework, external and same-project
  symbols, even if the target assembly has no ownership entry. Diagnostics identify
  the referenced symbol and the exact module/layer restriction. Module permissions
  and public contracts do not exempt a forbidden reference.
- AG006 checks explicit technology restrictions on the source assembly's layer,
  including framework/external and same-project targets. Allowed target namespace
  subtrees exempt only this layer's AG006 restrictions. There are no built-in bans.
  AG002, AG005 and AG006 report independently when their policies overlap; identical
  rule/use/restriction callbacks are not repeated, while distinct restrictions retain
  separate diagnostics. See the [deduplication policy](rule-catalog.md#ag006-domain-infrastructure).
- AG000 is an operational error for a missing, unreadable, duplicate or invalid
  snapshot or an unmapped current assembly. It is outside the canonical rule catalog
  and does not represent an architecture violation. Do not suppress it to claim coverage.

Configure reporting through normal `.editorconfig` settings; the analyzer does not
read the YAML rule severities or apply the solution's severity gate:

```ini
[*.cs]
dotnet_diagnostic.AG001.severity = error
dotnet_diagnostic.AG002.severity = warning
dotnet_diagnostic.AG004.severity = error
dotnet_diagnostic.AG005.severity = error
dotnet_diagnostic.AG006.severity = error
```

`none` disables a rule through the host. Aliases, global/static imports, partial types,
generic arguments, attributes and references to other projects are resolved through
Roslyn symbols. Imports alone do not produce findings; their bound uses do. Generated
code is included, including `.g.cs` files and generated-code attributes. Diagnostics
point to each bound simple name or predefined type keyword, so one statement can
contain several uses.

AG004, AG005 and AG006 use the same bound simple-name callbacks as AG001/AG002: base classes,
interfaces, type constraints, field/property/parameter/return type names, generic
names and arguments, predefined type keywords (`string`, `int`, `void`, etc.), object-creation type names, attributes, `typeof`, `nameof`,
casts, patterns and named member/extension-method uses. Named, constructed-generic and array aliases resolve to their actual target symbols
and nested type arguments at the alias use span. Partial declarations retain each use's own file/span;
there is no finding merely for declaring a partial type. Generated trees supplied by
the host are analyzed and reported with the same rules as authored trees.

This compiler surface does not walk implicit operations such as overloaded operators,
implicit conversions, target-typed `new`, inferred generic arguments or `foreach`/`await`
pattern methods. Bound explicit names within those expressions are still checked.
Use solution scanning for the broader semantic evidence described in the
[dependency graph guide](dependency-graph.md#semantic-reference-scope).

AG001/AG002/AG004 require assemblies in the snapshot. Other references, including framework
and external package assemblies, are ignored by those ownership rules; AG005 and AG006 check
explicit restrictions against all bound target assemblies. Missing entries for referenced repository
assemblies cannot be discovered from one compilation. Unused project references are
not checked. Compiler errors retain the host's own diagnostics, and unresolved symbols
do not establish a dependency. An empty analyzer result establishes neither complete
repository coverage nor compliance with AG003.

Each compilation loads its own snapshot once and concurrent callbacks only read it.
There is no static mutable policy cache. Cancellation is forwarded to AdditionalText
and semantic queries and observed during snapshot validation and semantic evaluation.
Cancellation is propagated, rather than converted into a clean result or AG000.

## Host compatibility and validation

The analyzer compiles against Roslyn 4.8.0, the API baseline mapped by Microsoft to
[Visual Studio 2022 17.8](https://learn.microsoft.com/en-us/visualstudio/extensibility/roslyn-version-support?view=visualstudio).
`netstandard2.0` and the absence of .NET 10 runtime dependencies permit loading in
that compiler API generation and later hosts. This is an API compatibility floor;
it does not extend vendor support for older SDKs or IDEs.

| Host | Validation |
| --- | --- |
| Roslyn 4.8 in-process analyzer driver / IDE-style workspace | Microsoft.CodeAnalysis.Testing semantic tests, multi-project and AdditionalFiles/editorconfig cases |
| Command-line compiler from the SDK pinned in `global.json` | Packaged consumer test in CI and before release publication on Linux, Windows and macOS |
| Visual Studio UI, Rider and other interactive IDEs | No UI-level validation in this repository; IDEs must supply Roslyn 4.8+ and AdditionalFiles |

The semantic suite covers AG005/AG006 framework/external restrictions and lookalikes, invalid configuration, cancellation, concurrent reuse,
generated sources and policy permissions. The package test checks the NuGet layout,
loads the installed analyzer through a real SDK build, requires located AG001/AG002/AG004
errors for a forbidden dependency, verifies that whole-module permission still yields
AG004, then declares a public contract through the AdditionalFile and requires a passing build.
It also adds an AG005 namespace restriction, requires a located diagnostic and checks
that removing the restriction restores the passing build. It checks an AG006 ban and
then restores the passing build through an explicit namespace exception. Run it locally with an installed SDK version:

```sh
python3 eng/test-analyzer-package.py artifacts/analyzers/ArchitectureGuardian.Analyzers.0.1.0.nupkg --sdk 10.0.402
```

CI results are distinct from local verification; interactive IDE UI behavior remains
a release validation limitation. The [semantic accuracy and parity guide](semantic-accuracy.md)
documents shared AG001/AG002/AG004/AG005/AG006 expectations, framework/configuration
matrix, generated sources, incomplete references and intentional engine differences.
