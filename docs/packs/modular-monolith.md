# Modular Monolith pack

`modular-monolith` packages logical-module, public-contract and implementation-boundary
conventions. It is an optional built-in [rule pack](../rule-packs.md); this page
documents version **1.0.0**.

## Logical modules are not projects

A **logical module** is an ownership boundary such as Orders or Billing. It usually
spans several projects (contracts, domain, application, infrastructure). A **project**
is a build unit. Architecture Guardian assigns every project to exactly one module
through `modules[].projects` and, independently, to one technical layer through
`layers[].projects`. Module identity never comes from a project, assembly or namespace
name; see [modules, layers and contracts](../concepts/modules-and-layers.md).

A **public contract** is a whole project that a module intentionally exposes. Other
modules may use types from contract projects only; every other project is the module's
implementation, even when its C# types are `public`.

## Supported layout

```text
src/Host/Shop.Host/Shop.Host.csproj                               module Host, layer Host
src/Modules/Orders/Orders.Contracts/Orders.Contracts.csproj       module Orders (contract api), layer Contracts
src/Modules/Orders/Orders.Domain/Orders.Domain.csproj             module Orders, layer Domain
src/Modules/Orders/Orders.Application/Orders.Application.csproj   module Orders, layer Application
src/Modules/Billing/Billing.Contracts/Billing.Contracts.csproj    module Billing (contract api), layer Contracts
src/Modules/Billing/Billing.Application/...                       module Billing, layer Application
src/Modules/Billing/Billing.Infrastructure/...                    module Billing, layer Infrastructure
```

The pack supplies:

| Declaration | Default selector | Notes |
| --- | --- | --- |
| Module `Host` | `src/Host/**/*.csproj` | Composition root; your policy grants its permissions |
| Layer `Contracts` | `src/Modules/**/*.Contracts.csproj` | May depend on nothing outside its layer |
| Layer `Domain` | `src/Modules/**/*.Domain.csproj` | — |
| Layer `Application` | `src/Modules/**/*.Application.csproj` | May use `Contracts`, `Domain` |
| Layer `Infrastructure` | `src/Modules/**/*.Infrastructure.csproj` | May use `Application`, `Contracts`, `Domain` |
| Layer `Host` | `src/Host/**/*.csproj` | May use every module layer |

| Rule | Severity | Enforces |
| --- | --- | --- |
| [AG001](../rule-catalog.md#ag001-module-dependencies) | `error` | Cross-module project dependencies need an explicit module or contract permission |
| [AG002](../rule-catalog.md#ag002-layer-dependencies) | `error` | The layer matrix inside and across modules |
| [AG003](../rule-catalog.md#ag003-module-cycles) | `error` | No cycles in the logical module graph, even through permitted contracts |
| [AG004](../rule-catalog.md#ag004-implementation-boundaries) | `error` | Code may use another module's types only from its declared contract projects |

## Declare modules and permitted contracts

The pack cannot know your modules. Declare each one with its projects and contract
projects, and list the contracts it may use:

```yaml
version: 1
extends:
  - pack: modular-monolith
    version: 1.0.0
modules:
  - id: Orders
    projects: ["src/Modules/Orders/**/*.csproj"]
    contracts:
      - id: api
        projects: ["src/Modules/Orders/Orders.Contracts/*.csproj"]
    allowedDependencies: ["contract:Billing/api"]
  - id: Billing
    projects: ["src/Modules/Billing/**/*.csproj"]
    contracts:
      - id: api
        projects: ["src/Modules/Billing/Billing.Contracts/*.csproj"]
  # Host is declared by the pack; grant only the contracts it composes.
  - id: Host
    allowedDependencies: ["contract:Billing/api", "contract:Orders/api"]
```

Permissions are direct and directional. `contract:Billing/api` permits only the
Billing contract project; `module:Billing` permits every Billing project for AG001,
but AG004 still rejects uses of Billing implementation types. Select both rules, as
the pack does, to enforce permission and encapsulation.

A module may expose several contracts, for example `api` for other modules and
`hosting` for module registration used only by `Host`. The pack has no layer for
`*.Hosting` projects, so declare one and let the `Host` layer use it:

```yaml
modules:
  - id: Billing
    projects: ["src/Modules/Billing/**/*.csproj"]
    contracts:
      - id: api
        projects: ["src/Modules/Billing/Billing.Contracts/*.csproj"]
      - id: hosting
        projects: ["src/Modules/Billing/Billing.Hosting/*.csproj"]
  - id: Host
    allowedDependencies: ["contract:Billing/api", "contract:Billing/hosting"]
layers:
  - id: Hosting
    projects: ["src/Modules/**/*.Hosting.csproj"]
    allowedDependencies: [Application, Contracts, Domain, Infrastructure]
  - id: Host
    allowedDependencies: [Application, Contracts, Domain, Hosting, Infrastructure]
```

Grant `contract:Billing/hosting` to `Host` only. Because AG004 accepts any declared
contract, AG001 permissions decide which modules may reference each contract.

## Exceptions and variations

- **Modules without a domain or infrastructure project**: the remaining selectors must
  still match at least one project solution-wide; exclude unused layers with
  `exclude: ["layer:Domain"]`.
- **Different folders** (`modules/`, `Modules/` at the root, `src/<Module>`): override the
  layer and `Host` selectors.
- **A shared kernel**: declare it as a module whose projects are also a contract, then
  grant `module:SharedKernel` or `contract:SharedKernel/kernel`, as in the
  [Vertical Slice pack](vertical-slice.md).
- **Single-project modules**: supported; the project is the module's implementation,
  so other modules cannot use it under AG004 unless you declare it as a contract.

## Fixtures

`tests/Fixtures/RulePacks/ModularMonolith` contains two multi-project modules with
contract assemblies and a host. `Compliant` passes. `AG004` makes Orders call a Billing
implementation type, producing AG001 and AG004 findings. `AG003` makes Billing use the
Orders contract while Orders uses Billing's, producing a cycle even though each
contract dependency is permitted.

## Version history

| Version | Changes |
| --- | --- |
| 1.0.0 | Initial Host module, module layers and AG001–AG004 selection |
