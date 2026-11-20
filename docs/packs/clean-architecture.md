# Clean Architecture pack

`clean-architecture` encodes a four-layer Clean Architecture whose project
dependencies point toward the domain. It is an optional built-in
[rule pack](../rule-packs.md); this page documents version **1.0.0**.

```yaml
version: 1
extends:
  - pack: clean-architecture
    version: 1.0.0
modules:
  - id: Shop
    projects: ["src/**/*.csproj"]
```

The pack declares layers only. Your policy declares at least one module, because
module ownership is specific to each repository.

## Supported layout

Every project must match exactly one layer. With the default selectors:

| Layer | Default selector | May depend on | Notes |
| --- | --- | --- | --- |
| `Domain` | `src/**/*.Domain.csproj` | — | AG006 restrictions below |
| `Application` | `src/**/*.Application.csproj` | `Domain` | Use cases and ports |
| `Infrastructure` | `src/**/*.Infrastructure.csproj` | `Application`, `Domain` | Adapters implementing ports |
| `Api` | `src/**/*.Api.csproj` | `Application`, `Domain`, `Infrastructure` | Entry point and composition root |

```text
src/Shop.Domain/Shop.Domain.csproj
src/Shop.Application/Shop.Application.csproj
src/Shop.Infrastructure/Shop.Infrastructure.csproj
src/Shop.Api/Shop.Api.csproj
```

Several projects may share a layer, for example `src/Billing.Domain` and
`src/Orders.Domain`. Same-layer dependencies are always allowed.

Selected rules:

| Rule | Severity | Enforces |
| --- | --- | --- |
| [AG002](../rule-catalog.md#ag002-layer-dependencies) | `error` | The layer matrix above |
| [AG006](../rule-catalog.md#ag006-domain-infrastructure) | `error` | `Domain` code must not use `Microsoft.AspNetCore`, `Microsoft.EntityFrameworkCore`, `System.Data` or `System.Net.Http` (including child namespaces) |

The pack does not select AG001/AG003/AG004; add them in your policy when the
solution has several modules, or combine it with the
[Modular Monolith pack](modular-monolith.md).

## Adapt it to your repository

Override only what differs. Fields you omit stay inherited:

```yaml
modules:
  # Every project needs a module, including tests; restating projects replaces the selector list.
  - id: Shop
    projects: ["src/**/*.csproj", "tests/**/*.csproj"]
layers:
  # The entry point is named Shop.Web; Api permissions are unchanged.
  - id: Api
    projects: ["src/Shop.Web/*.csproj"]
  # Allow an approved value namespace in the domain (replaces the whole AG006 object).
  - id: Domain
    domainInfrastructure:
      forbiddenNamespaces: [Microsoft.AspNetCore, Microsoft.EntityFrameworkCore, System.Data, System.Net.Http]
      allowedNamespaces: [System.Data.SqlTypes]
  # Test projects need a layer too.
  - id: Tests
    projects: ["tests/**/*.csproj"]
    allowedDependencies: [Api, Application, Domain, Infrastructure]
rules:
  AG006: warning   # adopt technology restrictions gradually
```

Common exceptions:

- **No separate API project** (for example a worker or a library): exclude the layer
  with `exclude: ["layer:Api"]` in the `extends` entry, or point its selector at your
  host project.
- **Stricter entry point**: override `Api.allowedDependencies` with
  `[Application, Infrastructure]` to forbid direct domain references.
- **Infrastructure that must not see use cases**: override
  `Infrastructure.allowedDependencies` with `[Domain]`.
- **Different suffixes** (`Core`, `UseCases`, `Persistence`): override each layer's
  `projects`; keep one selector per layer so unused selectors surface typos.

Every selector must match at least one project in a solution scan. If a layout
deliberately lacks a layer, exclude it rather than leaving an unmatched selector.

## Fixtures

`tests/Fixtures/RulePacks/CleanArchitecture` adopts this pack with a `Shop.Web`
override. The `Compliant` configuration passes; `AG002` adds an Application →
Infrastructure use and `AG006` a `System.Data.DataTable` use in the domain, each
failing the gate with only that rule.

## Version history

| Version | Changes |
| --- | --- |
| 1.0.0 | Initial Domain/Application/Infrastructure/Api layers with AG002 and Domain AG006 restrictions |
