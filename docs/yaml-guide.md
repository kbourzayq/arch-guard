# Write your architecture policy

The YAML DSL turns architectural decisions into explicit, reviewable constraints.
Start with a small complete policy for your actual project inventory, inspect its
resolved meaning, then add rules as your boundaries become clearer.

## 1. Choose the scope and policy location

For this walkthrough, your repository contains:

```text
Shop.slnx
architecture.guardian.yaml
src/
  Shop.Domain/Shop.Domain.csproj
  Shop.Application/Shop.Application.csproj
  Shop.Infrastructure/Shop.Infrastructure.csproj
```

The policy is beside `src/`, so every selector starts from there. If your input
also loads tests or other referenced projects, map those too. Do not copy these
selectors without adapting them to your inventory.

## 2. Declare ownership

One module is enough to begin. Layers describe the technical roles within it:

```yaml
version: 1
modules:
  - id: Shop
    projects: ["src/**/*.csproj"]
layers:
  - id: Domain
    projects: ["src/Shop.Domain/*.csproj"]
  - id: Application
    projects: ["src/Shop.Application/*.csproj"]
    allowedDependencies: [Domain]
  - id: Infrastructure
    projects: ["src/Shop.Infrastructure/*.csproj"]
    allowedDependencies: [Domain, Application]
rules:
  AG002: error
gate:
  failOn: error
```

This is a complete policy for exactly the three projects shown. It permits
Application → Domain and Infrastructure → Application/Domain. Domain has no
cross-layer permission. Same-layer dependencies are allowed.

`projects` selects whole project paths. `*` stays within one path segment; a whole
`**` segment spans directories. IDs and paths are case-sensitive on every OS.
Every loaded project must match exactly one module and layer, and every declared
selector must match at least one project. See [globs and validation](policy.md#project-identities-and-globs).

## 3. Inspect the policy's meaning

After authorizing your repository's build logic, restore the context and inspect:

```sh
dotnet restore Shop.slnx -p:Configuration=Release
archguard inspect Shop.slnx --policy architecture.guardian.yaml --configuration Release --trusted
archguard scan Shop.slnx --policy architecture.guardian.yaml --configuration Release --trusted
```

`inspect` lists ownership and permissions; it does not run architecture rules.
`scan` checks the selected rules and evaluates the gate. If ownership does not
resolve, fix the selectors first. See [outcomes](concepts/diagnostics.md).

## 4. Add business boundaries and contracts

As a system grows, split ownership into modules. Give each a deliberate public
API through a contract project, then grant exact outgoing access. The
[complete Orders/Billing policy](policy.md#document-shape) shows all required
fields; these are the relevant module declarations:

```yaml
modules:
  - id: Orders
    projects: ["src/Orders/**/*.csproj"]
    allowedDependencies: ["contract:Billing/api"]
  - id: Billing
    projects: ["src/Billing/**/*.csproj"]
    contracts:
      - id: api
        projects: ["src/Billing/Billing.Contracts.csproj"]
```

Enable AG001 to check those permissions and AG004 to reject access to another
module's implementation. Keep AG002 to enforce layer directions independently.
Enable AG003 to reject logical module cycles even when all edges have permissions.

This is a fragment, not a replacement for the earlier Shop policy. Changing the
inventory also requires corresponding layers/selectors. A contract covers whole
projects; C# public accessibility and a `.Contracts` suffix do not grant access.

## 5. Restrict technology dependencies

For an existing Domain layer, add an explicit AG006 restriction. For example:

```yaml
# Add this property to the existing Domain layer declaration.
domainInfrastructure:
  forbiddenNamespaces: [System.Data]
  allowedNamespaces: [System.Data.SqlTypes]
```

Also add `AG006: error` to `rules`. This forbids Domain uses of `System.Data` and
its descendants while allowing the narrower `System.Data.SqlTypes` subtree for
AG006. No technology is banned automatically. The exception does not override
AG002 layer checks or separate AG005 restrictions.

Use AG005 for explicit module/layer bans that need no AG006 exception semantics.
Assembly restrictions use assembly simple names, not NuGet package IDs. Namespace
matching respects segment boundaries: `System.Net` does not match `System.NetLike`.

## 6. Choose severities and the gate

```yaml
rules:
  AG001: error
  AG002: error
  AG003: warning
  AG004: error
  AG005: warning
  AG006: error
gate:
  failOn: error
```

This selection fragment runs all six rules; warnings remain visible below an
error-threshold gate. Omit a rule to leave it inactive. `enabled`, `off`,
per-rule scope selectors and policy inheritance are not supported YAML fields.
Unknown keys and rule IDs fail validation instead of being ignored.

For gradual adoption, review current findings and choose [baselines](baselines.md)
or [Git comparisons](changes.md). These are scan options, not YAML fields.

## Reference and practical examples

- [YAML reference](policy.md): exact fields, defaults, selectors and restrictions.
- [Rule catalog](rule-catalog.md): examples, evidence and corrective options.
- [Use cases](use-cases.md): downloadable examples and adoption workflows.
- [Compiler setup](compiler-analyzer.md): share this YAML with IDE/build feedback.

Review policy changes as architectural decisions. Broad permissions, new contract
projects and severity changes can alter what the gate permits even when no C# file changes.
