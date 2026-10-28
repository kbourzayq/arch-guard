# Compiler analyzer

Add architecture feedback to C# compilation with `ArchitectureGuardian.Analyzers`.
It targets `netstandard2.0`, uses the host's current compilation and reads an explicit
YAML policy supplied as an AdditionalFile, shared with CLI and MCP.
Legacy XML snapshots remain supported for one transition release.

The analyzer checks AG001, AG002, AG004, AG005 and AG006. It shares their permission
semantics with the solution engine, but reports bound type/member uses at source
locations rather than grouping evidence by project pair. Counts and diagnostic
identities are not interchangeable across engines. It does not restore
projects, open a workspace or establish whole-solution cycle coverage.

Keep CLI or MCP solution scans as the full architecture gate. See
[engine scope and limits](semantic-accuracy.md) before choosing compiler-only checks.

## Install and distribute policy

### 1. Choose the package

Use published `ArchitectureGuardian.Analyzers` version `0.2.0` from NuGet. No
Architecture Guardian checkout or source build is required. All three integration
packages share a [version and compatibility policy](releases.md).

For development builds, [create a local package](installation.md#build-a-local-compiler-analyzer-package)
and add its output directory as a source in your consuming repository's NuGet
configuration. Retain its normal public/private feeds. Do not commit a developer's
absolute package-directory path to shared configuration.

### 2. Reference the analyzer and policy

In the consuming repository, add the following to a root `Directory.Build.props`
(or merge the ItemGroup into your existing file). The package applies to all
participating projects. YAML project selectors determine ownership; packaged build
targets supply evaluated assembly identities and paths.

```xml
<Project>
  <ItemGroup>
    <PackageReference Include="ArchitectureGuardian.Analyzers" Version="0.2.0" PrivateAssets="all" />
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)architecture.guardian.yaml" />
  </ItemGroup>
</Project>
```

If the consuming repository uses central package management, declare
`<PackageVersion Include="ArchitectureGuardian.Analyzers" Version="0.2.0" />` in
`Directory.Packages.props` and omit `Version` from `PackageReference` above.

Use the same `architecture.guardian.yaml` as your CLI/MCP scans. YAML support
requires a build containing issue #83 or a release including it; published `0.2.0`
consumers should use the [legacy snapshot setup](#compiler-policy-snapshot).
Authorize the consuming
repository's build logic and run from its root, replacing `MyApp.slnx`:

```sh
dotnet restore MyApp.slnx
dotnet build MyApp.slnx --no-restore
```

Restore uses your configured package feeds. For a locally built package, add its
source first; the [local package guide](installation.md) explains output paths.
A successful configured build has no AG000 or analyzer-load errors; architecture
findings follow the severities you select. Package installation alone is not a
configured check.

YAML supplies rule selection and default severity for supported compiler rules.
Optional `.editorconfig` settings override reporting severity or suppress selected rules.

The package bundles the analyzer, portable policy reader and YamlDotNet assemblies
under `analyzers/dotnet/cs`, plus `buildTransitive` metadata targets. All compiler
dependencies support .NET Standard 2.0; no CLI or .NET 10 runtime is required.
Roslyn assemblies come from the host.
`PrivateAssets="all"` prevents unintended distribution to consumers of your own package.

## YAML ownership and migration

Include exactly one explicit `architecture.guardian.yaml`. Do not include both
YAML and XML or duplicate policy files: these produce AG000. Invalid YAML never
falls back to XML. No upward policy search or automatic CLI invocation occurs.

The package forwards the current project path and resolved direct/transitive
project-reference metadata through the SDK's generated analyzer configuration.
Referenced project files appear as additional inputs carrying evaluated assembly
names/identities and reference paths; the analyzer reads their metadata, not their
XML content. Install the package through root props in participating projects so
clean design-time project references also export their evaluated names. Custom
`AssemblyName`, configuration/framework-specific outputs and in-memory references
retain project-path ownership. Build artifacts stay under the normal `obj/` folders.

Each observed project must match exactly one module and layer and at most one
whole-project contract relative to the YAML directory. Missing/conflicting ownership
produces AG000. Full-inventory checks, such as unused selectors, remain with CLI/MCP.
Framework and package references have no project ownership, but AG005/AG006 still
check their bound symbol uses. The analyzer does not open a workspace or evaluate
project files. Custom compiler hosts must provide equivalent analyzer options.

To migrate, replace the XML `AdditionalFiles` entry with YAML, remove the maintained
snapshot when unused, and review existing `.editorconfig` overrides. Omitted YAML
rules are inactive even if `.editorconfig` supplies a severity. AG003 is accepted in
the shared document but evaluated only by solution scans; `gate.failOn` does not
change compiler reporting. Prefer YAML for new installations.

The first minor release containing YAML support retains XML for one transition
release. XML removal is planned for the following minor release.

## Compiler policy snapshot

This is the legacy format, including the setup for published `0.2.0`. Use
`<AdditionalFiles Include="$(MSBuildThisFileDirectory)architecture.guardian.compiler.xml" />`
with the analyzer package instead of the YAML entry above. XML mode preserves its
existing `.editorconfig`-based reporting behavior during the transition release.

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
  policy or an unmapped current assembly. It is outside the canonical rule catalog
  and does not represent an architecture violation. Do not suppress it to claim coverage.

YAML selects the supported compiler rules and their default severities. Normal
`.editorconfig` settings can override severity or suppress selected rules; XML mode
continues to use the warning defaults and `.editorconfig` settings. Neither mode
applies the solution severity gate:

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

AG001/AG002/AG004 require project ownership from YAML/build metadata or legacy XML. Other references, including framework
and external package assemblies, are ignored by those ownership rules; AG005 and AG006 check
explicit restrictions against all bound target assemblies. Missing or ambiguous observed project ownership produces AG000 in YAML mode.
Legacy XML cannot discover missing referenced-assembly entries. Unused project references are
not checked. Compiler errors retain the host's own diagnostics, and unresolved symbols
do not establish a dependency. An empty analyzer result establishes neither complete
repository coverage nor compliance with AG003.

Each compilation loads its own policy once and concurrent callbacks only read it.
There is no static mutable policy cache. Cancellation is forwarded to AdditionalText
and semantic queries and observed during policy validation and semantic evaluation.
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
python3 eng/test-analyzer-package.py artifacts/analyzers/ArchitectureGuardian.Analyzers.0.2.0.nupkg --sdk 10.0.402
```

CI results are distinct from local verification; interactive IDE UI behavior remains
a release validation limitation. The [semantic accuracy and parity guide](semantic-accuracy.md)
documents shared AG001/AG002/AG004/AG005/AG006 expectations, framework/configuration
matrix, generated sources, incomplete references and intentional engine differences.
