# ABP Framework pack

`abp` brings [ABP Framework](https://abp.io)'s layered architecture into your CI gate. It is
an optional built-in [rule pack](../rule-packs.md); this page documents version **1.0.0**.

ABP solutions follow a well-defined layering: `Domain.Shared`, `Domain`,
`Application.Contracts`, `Application`, a database provider integration, `HttpApi`,
`HttpApi.Client` and a host. The pack maps those projects to layers and enforces their
dependency directions. It also checks the ABP conventions that project references alone
cannot show: module `[DependsOn]` declarations, DTO boundaries, repository access,
application service authorization and multi-tenant isolation.

```yaml
# architecture.guardian.yaml at your ABP solution root
version: 1
extends:
  - pack: abp
    version: 1.0.0
modules:
  - id: BookStore
    projects: ["src/**/*.csproj", "test/**/*.csproj"]
layers:
  - id: Host
    projects: ["src/Acme.BookStore.Web/*.csproj", "src/Acme.BookStore.DbMigrator/*.csproj"]
  - id: Tests
    projects: ["test/**/*.csproj"]
```

```sh
dotnet restore Acme.BookStore.sln
archguard scan Acme.BookStore.sln --policy architecture.guardian.yaml --trusted
```

You can also let [discovery](../discovery.md#abp-framework-solutions) write this policy:
`archguard init --discover` recognizes ABP solutions and proposes `extends: abp`.

## Layers

The pack follows ABP's [layered solution
template](https://abp.io/docs/latest/solution-templates/layered-web-application). Selectors
use `**/`, so they match `src/`, `modules/*/src/` and other folders.

```text
Host                     ──► every layer below
HttpApi, HttpApiClient   ──► ApplicationContracts
Application              ──► ApplicationContracts, Domain
Persistence              ──► Domain
every layer              ──► DomainShared
```

| Layer | Default selector | May depend on | Technology restrictions (AG006) |
| --- | --- | --- | --- |
| `DomainShared` | `**/*.Domain.Shared.csproj` | — | No database provider or MVC |
| `Domain` | `**/*.Domain.csproj` | `DomainShared` | No database provider or MVC |
| `ApplicationContracts` | `**/*.Application.Contracts.csproj` | `DomainShared` | No database provider or MVC |
| `Application` | `**/*.Application.csproj` | `ApplicationContracts`, `Domain`, `DomainShared` | No database provider or MVC |
| `Persistence` | `**/*.EntityFrameworkCore.csproj` | `Domain`, `DomainShared` | — |
| `HttpApi` | `**/*.HttpApi.csproj` | `ApplicationContracts`, `DomainShared` | No database provider |
| `HttpApiClient` | `**/*.HttpApi.Client.csproj` | `ApplicationContracts`, `DomainShared` | No database provider |
| `Host` | *your policy* | every layer above | — |
| `Tests` | *your policy* | every layer, including `Host` | — |

"Database provider" means `Microsoft.EntityFrameworkCore`, `MongoDB`,
`Volo.Abp.EntityFrameworkCore` and `Volo.Abp.MongoDB`. "MVC" means
`Microsoft.AspNetCore.Mvc` and `Volo.Abp.AspNetCore.Mvc`. Application services use
`[Authorize]` from `Microsoft.AspNetCore.Authorization`, which stays allowed.

Host and test project names vary (`*.Web`, `*.HttpApi.Host`, `*.Blazor`, `*.AuthServer`,
`*.DbMigrator`), so the pack declares those layers' permissions and your policy names
their projects. If the solution has no tests, exclude the layer instead.

## Rules

| Rule | Severity | Confidence | Enforces |
| --- | --- | --- | --- |
| [AG002](../rule-catalog.md#ag002-layer-dependencies) | `error` | — | The layer directions above |
| [AG006](../rule-catalog.md#ag006-domain-infrastructure) | `error` | — | Provider-independent domain and application layers |
| [ABP001](../rule-catalog.md#abp001-module-dependency-declarations) | `error` | `proven` | Each module's `[DependsOn]` reaches the modules of the projects it references |
| [ABP002](../rule-catalog.md#abp002-application-service-dto-boundaries) | `error` | `proven` | Application services exchange DTOs, never entities |
| [ABP003](../rule-catalog.md#abp003-repository-access-layers) | `error` | `proven` | Repositories are used only in `abp.repositoryLayers` |
| [ABP004](../rule-catalog.md#abp004-application-service-authorization) | `warning` | `potential` | Exposable application service methods declare authorization |
| [ABP005](../rule-catalog.md#abp005-tenant-isolation-bypass) | `warning` | `potential` | Code does not disable multi-tenant isolation |

The pack sets `abp.repositoryLayers: [Application, Domain, Persistence, Tests]` and no
tenant-filter bypass layers. Add module rules yourself: the pack declares no logical
modules, so select AG001, AG003 and AG004 once you declare them.

### Security heuristics and their limits

ABP004 and ABP005 are security heuristics. Their findings are always `potential` and come
with explicit runtime limitations, so treat them as review prompts, not as proof:

- **ABP004** sees `[Authorize]`/`[AllowAnonymous]`, imperative permission checks and CRUD
  policy names in source. It cannot see conventional-controller configuration, global
  authorization policies, middleware or checks inside called helpers. Confirm effective
  authorization with integration tests.
- **ABP005** sees `DataFilter.Disable<IMultiTenant>()` and `CurrentTenant.Change(null)`.
  Whether another tenant's data is read or returned depends on runtime data. Confirm tenant
  isolation with tests, and allow deliberate host-side work through `abp.tenantFilterBypassLayers`.

Each rule's supported cases and blind spots are listed in the
[rule catalog](../rule-catalog.md#abp-framework-rules-abp001abp005) and returned by MCP
`explain_violation` rule guidance.

## Scope and override the conventions

Everything the pack supplies can be changed in your policy, field by field:

```yaml
extends:
  - pack: abp
    version: 1.0.0
    exclude: ["layer:HttpApiClient", "rule:ABP004"]   # drop what your solution does not use
layers:
  - id: Persistence
    projects: ["**/*.MongoDB.csproj"]                 # MongoDB instead of EF Core
  - id: Host
    projects: ["src/Acme.BookStore.HttpApi.Host/*.csproj"]
abp:
  tenantFilterBypassLayers: [Host]                    # the DbMigrator seeds every tenant
rules:
  ABP005: error                                       # make tenant isolation blocking
  AG001: error
```

- **Severities**: restate a rule with another severity; omit nothing else.
- **Exclusions**: `exclude: ["rule:ABP00N"]` drops a rule; `exclude: ["layer:ID"]` drops a
  layer and removes it from every permission and `abp` list.
- **Settings**: `abp.repositoryLayers` and `abp.tenantFilterBypassLayers` replace the
  pack's lists independently. See the [policy reference](../policy.md#abp-framework-layers-abp003-abp005).
- **Individual findings**: accept reviewed exceptions in a [baseline](../baselines.md).

Every selector must match at least one project. If your solution lacks a pack layer, such
as `HttpApiClient` in a module-less API, exclude it.

## Modular ABP solutions

In a modular ABP application each module (`Acme.Catalog`, `Acme.Ordering`, …) has its own
layered projects. Declare one logical module per ABP module, and publish its
`Application.Contracts` and `Domain.Shared` projects as the contract other modules may use:

```yaml
modules:
  - id: Catalog
    projects: ["modules/catalog/**/*.csproj"]
    contracts:
      - id: api
        projects: ["modules/catalog/src/Acme.Catalog.Application.Contracts/*.csproj",
                   "modules/catalog/src/Acme.Catalog.Domain.Shared/*.csproj"]
  - id: Ordering
    projects: ["modules/ordering/**/*.csproj"]
    allowedDependencies: ["contract:Catalog/api"]
rules:
  AG001: error
```

ABP001 then checks that a module's `[DependsOn]` covers each referenced module, while AG001
checks that the reference is allowed at all.

## Support matrix

| ABP Framework | .NET | Status |
| --- | --- | --- |
| 10.7.x | 10 | **Verified**: the test fixture restores ABP 10.7.1 and every rule is tested against it |
| 10.0.x | 10 | **Verified manually**: the same fixture pinned to 10.0.0 produces identical findings |
| 10.1–10.6 | 10 | Expected to work: every symbol the rules use is unchanged across 10.x |
| 8.x, 9.x | 8, 9 | Expected to work, not tested; discovery asks you to review findings |
| 11.x | 11 | Not tested: ABP 11's ASP.NET Core and EF Core packages target .NET 11 only, and this release is validated with .NET 10 SDKs |
| 7.x and earlier | — | Not tested |

The rules match these ABP symbols by namespace and metadata name: `AbpModule` and
`DependsOnAttribute` (`Volo.Abp.Modularity`), `IApplicationService`,
`IntegrationServiceAttribute` and the CRUD application service bases
(`Volo.Abp.Application.Services`), `IEntity` (`Volo.Abp.Domain.Entities`), `IRepository`
(`Volo.Abp.Domain.Repositories`), `RemoteServiceAttribute` (`Volo.Abp`), `IDataFilter`
(`Volo.Abp.Data`), and `IMultiTenant` and `ICurrentTenant` (`Volo.Abp.MultiTenancy`).
Authorization attributes come from `Microsoft.AspNetCore.Authorization`.

ABP Studio's microservice template splits services into separate solutions; scan each
service solution with its own policy. The rules do not claim whole-system coverage from one
solution.

## Fixtures

`tests/Fixtures/Workspaces/Abp` is a BookStore solution restored with ABP 10.7.1. The
`Catalog` module is fully compliant and produces no finding. The `Ordering` module and the
`Acme.BookStore.Web` host seed one case per rule: a missing `[DependsOn]`, an entity
returned and nested in a DTO, a host controller using `IRepository<Book, Guid>`,
unauthorized methods including inherited CRUD methods, and both tenant-isolation bypasses.
Its policy extends this pack and excludes `layer:Tests`.

## Version history

| Version | Changes |
| --- | --- |
| 1.0.0 | Initial ABP layered template layers, AG002/AG006 restrictions and ABP001–ABP005 selection |
