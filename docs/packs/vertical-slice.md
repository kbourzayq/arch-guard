# Vertical Slice pack

`vertical-slice` supports declared feature or slice boundaries without imposing a
layer-first structure. It is an optional built-in [rule pack](../rule-packs.md);
this page documents version **1.0.0**.

## Scope

A **slice** is a logical module whose projects implement one feature area, such as
Catalog or Ordering. Slices may share only:

- the **shared kernel**, a module whose projects are all declared as its public
  contract, so AG004 permits shared abstractions;
- another slice's declared **contract projects**, when your policy approves that
  dependency.

Boundaries are **project-granular**. Namespace dependencies inside a slice's projects
are ordinary implementation details and are never findings: a handler in
`Catalog.Feature.GetProduct` may freely use `Catalog.Feature.Products`. The pack does
not infer slices from namespaces or folders and does not treat every namespace
dependency as a violation. Slices that live as folders inside one project cannot be
separated by this pack; split them into projects or keep them as one module.

## Supported layout

```text
src/Host/Shop.Host/Shop.Host.csproj                               module Host, layer Host
src/SharedKernel/Shop.SharedKernel/Shop.SharedKernel.csproj       module SharedKernel (contract kernel), layer SharedKernel
src/Features/Catalog/Catalog.Contracts/Catalog.Contracts.csproj   module Catalog (contract api), layer Slices
src/Features/Catalog/Catalog.Feature/Catalog.Feature.csproj       module Catalog, layer Slices
src/Features/Ordering/Ordering.Feature/Ordering.Feature.csproj    module Ordering, layer Slices
```

The pack supplies:

| Declaration | Default selector | Notes |
| --- | --- | --- |
| Module `SharedKernel` | `src/SharedKernel/**/*.csproj` | Whole module is contract `kernel` |
| Module `Host` | `src/Host/**/*.csproj` | Composition root; your policy grants its permissions |
| Layer `SharedKernel` | `src/SharedKernel/**/*.csproj` | Depends on nothing else |
| Layer `Slices` | `src/Features/**/*.csproj` | May use `SharedKernel` |
| Layer `Host` | `src/Host/**/*.csproj` | May use `SharedKernel`, `Slices` |

The coarse layers only keep slices independent of the host and the kernel independent
of slices; they do not impose layers inside a slice.

| Rule | Severity | Enforces |
| --- | --- | --- |
| [AG001](../rule-catalog.md#ag001-module-dependencies) | `error` | Slice dependencies need explicit approval |
| [AG002](../rule-catalog.md#ag002-layer-dependencies) | `error` | Kernel → slices → host direction |
| [AG003](../rule-catalog.md#ag003-module-cycles) | `error` | No cycles between slices |
| [AG004](../rule-catalog.md#ag004-implementation-boundaries) | `error` | No cross-slice implementation access |

## Declare slices and approved dependencies

```yaml
version: 1
extends:
  - pack: vertical-slice
    version: 1.0.0
modules:
  - id: Catalog
    projects: ["src/Features/Catalog/**/*.csproj"]
    contracts:
      - id: api
        projects: ["src/Features/Catalog/Catalog.Contracts/*.csproj"]
    allowedDependencies: ["module:SharedKernel"]
  - id: Ordering
    projects: ["src/Features/Ordering/**/*.csproj"]
    allowedDependencies: ["module:SharedKernel", "contract:Catalog/api"]
  - id: Host
    allowedDependencies: ["contract:Catalog/api"]
```

Slice identity is the module `id`; slice membership is the `projects` selector.
Approve a slice dependency by granting the target's contract, never its whole module,
so implementation stays private. A slice without contracts exposes nothing to other
slices or the host.

## Exceptions and variations

- **A renamed shared kernel** (`Common`, `BuildingBlocks`): override the `SharedKernel`
  module, its `kernel` contract and the layer selectors.
- **Part of the kernel is private**: override the `kernel` contract's `projects` with
  the public projects only.
- **No shared kernel**: exclude it with `exclude: ["module:SharedKernel", "layer:SharedKernel"]`.
  The pack removes its own permissions that target the kernel, but not yours: also
  remove `module:SharedKernel` from each slice's `allowedDependencies`.
- **Endpoint registration in the host**: expose each slice's registration project as a
  contract (for example `hosting`) and grant it only to `Host`, as described in the
  [Modular Monolith pack](modular-monolith.md#declare-modules-and-permitted-contracts).

## Fixtures

`tests/Fixtures/RulePacks/VerticalSlice` contains two slices, a shared kernel and a host.
`Compliant` passes with shared-kernel entities, a Catalog contract used by Ordering and
intra-slice namespace dependencies. `AG004` makes Ordering use Catalog's repository,
producing AG001 and AG004 findings.

## Version history

| Version | Changes |
| --- | --- |
| 1.0.0 | Initial SharedKernel and Host modules, coarse layers and AG001–AG004 selection |
