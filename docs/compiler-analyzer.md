# Compiler analyzer

F12 adds `ArchitectureGuardian.Analyzers`, a C# Roslyn analyzer targeting
`netstandard2.0`. It uses the host's current compilation and an explicit compiler
policy AdditionalFile. It never discovers a repository, loads YAML, opens a workspace,
restores projects or invokes CLI. AG001 and AG002 use the same permission predicates,
IDs, titles and help URLs as Core. Findings identify a bound type/member use and its
source span; the solution engine instead groups evidence by project pair. Diagnostic
counts and snapshot identities are therefore not interchangeable across engines.

## Install and distribute policy

Build a local package (publication to nuget.org is a separate release step):

```sh
dotnet pack src/ArchitectureGuardian.Analyzers --configuration Release --output artifacts/analyzers
```

Add that directory to your NuGet sources, then reference the package and one snapshot
in each participating C# project, usually through a repository `Directory.Build.props`:

```xml
<ItemGroup>
  <PackageReference Include="ArchitectureGuardian.Analyzers" Version="0.1.0" PrivateAssets="all" />
  <AdditionalFiles Include="$(MSBuildThisFileDirectory)architecture.guardian.compiler.xml" />
</ItemGroup>
```

The package contains only `analyzers/dotnet/cs/ArchitectureGuardian.Analyzers.dll`;
it has no runtime library or NuGet dependencies. Roslyn assemblies come from the host.
`PrivateAssets="all"` prevents unintended distribution to consumers of your own package.

Commit the compiler snapshot alongside the authoritative `architecture.guardian.yaml`.
For F12, maintain the snapshot explicitly from the YAML's resolved project inventory:
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
  <Module id="Orders" allows="contract:Billing/api" />
  <Module id="Billing" />
  <Layer id="Application" allows="Contracts" />
  <Layer id="Contracts" />
  <Layer id="Infrastructure" allows="Contracts" />
  <Assembly name="Orders.Application" module="Orders" layer="Application" />
  <Assembly name="Billing.Contracts" module="Billing" layer="Contracts" contract="api" />
  <Assembly name="Billing.Infrastructure" module="Billing" layer="Infrastructure" />
</ArchitectureGuardian>
```

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
- AG000 is an operational error for a missing, unreadable, duplicate or invalid
  snapshot or an unmapped current assembly. It is outside the canonical rule catalog
  and does not represent an architecture violation. Do not suppress it to claim coverage.

Configure reporting through normal `.editorconfig` settings; the analyzer does not
read the YAML rule severities or apply the solution's severity gate:

```ini
[*.cs]
dotnet_diagnostic.AG001.severity = error
dotnet_diagnostic.AG002.severity = warning
```

`none` disables a rule through the host. Aliases, global/static imports, partial types,
generic arguments, attributes and references to other projects are resolved through
Roslyn symbols. Imports alone do not produce findings; their bound uses do. Generated
code is included, including `.g.cs` files and generated-code attributes. Diagnostics
point to each bound simple name, so one statement can contain several uses.

Only assemblies in the snapshot are in scope. Other references, including framework
and external package assemblies, are ignored; missing entries for referenced repository
assemblies cannot be discovered from one compilation. Unused project references are
not checked. Compiler errors retain the host's own diagnostics, and unresolved symbols
do not establish a dependency. An empty analyzer result establishes neither complete
repository coverage nor compliance with AG003 or the planned AG004–AG006 rules.

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
| SDK 8.0.100, 9.0.100, 10.0.301 command-line compiler | Packaged consumer test in CI on Linux, Windows and macOS |
| Visual Studio UI, Rider and other interactive IDEs | No UI-level validation in this repository; IDEs must supply Roslyn 4.8+ and AdditionalFiles |

The semantic suite covers invalid configuration, cancellation, concurrent reuse,
generated sources and policy permissions. The package test checks the NuGet layout,
loads the installed analyzer through a real SDK build, requires located AG001/AG002
errors for a forbidden dependency, then updates only the AdditionalFile and requires
a passing build. Run it locally with an installed SDK version:

```sh
python3 eng/test-analyzer-package.py artifacts/analyzers/ArchitectureGuardian.Analyzers.0.1.0.nupkg --sdk 10.0.301
```

CI results are distinct from local verification; interactive IDE UI behavior remains
a release validation limitation. F16 will extend cross-engine parity checks where
both engines have sufficient context.
