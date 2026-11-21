# Rule packs and `extends`

A rule pack is a versioned, reusable fragment of an architecture policy: default
layers, modules, public contracts, technology restrictions, rule selections and a
gate. A repository policy opts in to packs with `extends`, then overrides only the
settings that differ in that repository. Packs never change rule semantics; they
configure the same AG001–AG006 rules (and may select ASP001–ASP005, EF001–EF003 and ABP001–ABP005, including `entityFrameworkCore` and `abp` settings) described in the [rule catalog](rule-catalog.md).

Built-in packs:

| Pack | Version | Use it for | Guide |
| --- | --- | --- | --- |
| `abp` | `1.0.0` | ABP Framework layered solutions: the startup template's layers plus ABP module, DTO, repository, authorization and multi-tenancy conventions | [ABP Framework pack](packs/abp.md) |
| `clean-architecture` | `1.0.0` | Domain/Application/Infrastructure/Api layers pointing inward | [Clean Architecture pack](packs/clean-architecture.md) |
| `modular-monolith` | `1.0.0` | Multi-project logical modules talking through contract projects | [Modular Monolith pack](packs/modular-monolith.md) |
| `vertical-slice` | `1.0.0` | Feature slices isolated from each other, sharing a kernel | [Vertical Slice pack](packs/vertical-slice.md) |

## Extend a pack

```yaml
# architecture.guardian.yaml
version: 1
extends:
  - pack: clean-architecture
    version: 1.0.0
modules:
  - id: Shop
    projects: ["src/**/*.csproj"]
layers:
  # Override one selector; the pack's permissions for Api remain.
  - id: Api
    projects: ["src/Shop.Web/*.csproj"]
```

Each `extends` entry has these fields:

| Field | Required | Meaning |
| --- | --- | --- |
| `pack` | Yes | Pack identity: lowercase letters, digits and single hyphens, at most 64 characters |
| `version` | Yes | The exact `MAJOR.MINOR.PATCH` version. Ranges, wildcards and prerelease labels are rejected |
| `path` | No | Directory of a **local** pack, relative to the root policy directory. Omit it for a built-in pack |
| `exclude` | No | Declarations or rules removed from this pack's contribution: `layer:ID`, `module:ID` or `rule:ID`, such as `rule:AG006` or `rule:ABP004` |

When a policy has `extends`, `modules`, `layers` and `rules` become optional in the
document itself, and an entry may name only an `id` plus the fields it overrides.
After composition the policy must still be complete: at least one module, layer and
selected rule, and `projects` for every module, contract and layer. Selector coverage
and ownership checks then apply exactly as for a hand-written policy; see the
[policy reference](policy.md#project-identities-and-globs). A policy without
`extends` keeps the strict v1 shape, so existing policies are unaffected.

## Composition semantics

Composition is deterministic for the same policy, packs and engine version.

1. **Resolution.** Each `extends` entry is resolved depth-first. A pack is loaded once;
   a diamond (two packs extending the same base) reuses it.
2. **Exclusions.** `exclude` removes a declaration and its fields from that pack's
   contribution. Removing a layer also removes it from the pack's layer
   `allowedDependencies`, `entityFrameworkCore.persistenceLayers` and `abp` layer
   lists; removing a module removes `module:ID` and `contract:ID/*` permissions.
   Excluding something the pack does not supply is an error.
3. **Siblings.** Packs listed in the same `extends` array have equal precedence.
   Different declarations combine. If two siblings set the **same setting** to
   different values, composition fails and names both packs and the setting, unless
   the extending document sets that setting itself. List order never changes the result.
4. **Overrides.** The extending document replaces inherited settings **field by
   field**: a module or layer's `projects`, `allowedDependencies`,
   `forbiddenAssemblies`, `forbiddenNamespaces`, `domainInfrastructure` (as one
   object), each contract's `projects`, each rule severity, `gate.failOn`, and each
   `entityFrameworkCore` and `abp` list.
   Lists are replaced, not appended. Fields you do not mention remain inherited.
5. **Validation.** The composed policy is validated: unique identities, known
   permission targets, selectors and names. Errors point to the document and field
   that supplied the value, including `builtin:<id>@<version>/pack.yaml` for
   embedded packs.

Packs only contribute settings. Only the document that extends a pack can remove
part of that pack's contribution, with `exclude`. Rules that no document selects stay
disabled. Packs never enable baselines, incomplete-analysis gates or custom options.

### Failures

Composition fails with a located `PolicyConfigurationException` (CLI exit `2`, MCP
"invalid policy") instead of silently reducing coverage when:

- a pack is unknown, a built-in version is not embedded (the error lists available
  versions), or a local manifest is missing;
- a manifest's `id`/`version` differs from the pinned reference;
- the same pack is referenced with two versions or sources anywhere in the graph;
- `extends` forms a cycle (the error shows the chain), exceeds 16 levels or 32 packs;
- a manifest targets another policy schema, requires a newer engine, configures a
  rule this engine does not provide, or contains unknown fields;
- sibling packs conflict without an explicit override.

## Resolution and pinning

**Built-in packs** are embedded in the policy reader shared by the CLI, MCP server
and Roslyn analyzer, so every engine resolves the same content without network or
file access. Each released version of a built-in pack is immutable and remains
embedded; changes ship as a new pack version. Upgrade by changing `version` after
reviewing the version history in the pack's guide.

**Local packs** live in the repository and are referenced with `path`:

```yaml
extends:
  - pack: acme-standards
    version: 2.1.0
    path: build/architecture/acme-standards   # reads build/architecture/acme-standards/pack.yaml
```

The path is relative to the **root** policy's directory, uses `/`, and may not contain
`.`, `..`, empty segments, drive prefixes or backslashes. CLI and MCP refuse paths that
traverse symbolic links or reparse points and manifests larger than 256 KiB. A local
pack may extend built-in packs and other local packs (paths are still relative to
the root policy); built-in packs may extend only built-in packs. Version a shared
local pack by committing it with the policy, or by vendoring a reviewed copy of
another repository's pack directory; there is no download or package-feed resolution.

Git [change analysis](changes.md) loads both snapshots with their own committed packs.

## Write a local pack

```yaml
# build/architecture/acme-standards/pack.yaml
# yaml-language-server: $schema=../../../docs/schema/rule-pack.v1.schema.json
manifest: 1
id: acme-standards
version: 2.1.0
description: Company-wide technology restrictions.
requires:
  policySchema: 1      # the policy schema version the fragment uses
  engine: 0.4.1        # minimum Architecture Guardian version
rules:                 # rule IDs this pack selects, with default severities
  AG005: error
layers:
  - id: Domain
    forbiddenNamespaces: [System.Net]
```

`manifest`, `id`, `version`, `requires` and a nonempty `rules` map are required.
`description`, `extends`, `modules`, `layers`, `gate`, `entityFrameworkCore` and `abp` are optional; module and
layer entries use the policy fields and may be partial. The
[manifest schema](schema/rule-pack.v1.schema.json) supports editor validation.

## Trust model

Packs are declarative YAML. Architecture Guardian does **not** load assemblies,
analyzers, scripts or any other executable extension code from a pack; unknown
manifest fields such as `assemblies` are rejected. Rule logic exists only in the
engine. Treat a local pack like the policy itself: it is repository content that can
weaken or strengthen enforcement, so review changes to it. Workspace loading keeps
its separate [trust requirement](workspace-loading.md) for MSBuild evaluation.

## Compiler hosts

The analyzer resolves built-in packs directly. It does not read files, so include
each local pack manifest as an `AdditionalFiles` item next to the policy:

```xml
<ItemGroup>
  <AdditionalFiles Include="$(MSBuildThisFileDirectory)architecture.guardian.yaml" />
  <AdditionalFiles Include="$(MSBuildThisFileDirectory)build/architecture/acme-standards/pack.yaml" />
</ItemGroup>
```

A missing manifest produces an `AG000` configuration diagnostic. Compiler analysis
does not check unused selectors, so a pack layer without projects matters only to
solution scans; see the [analyzer guide](compiler-analyzer.md).
