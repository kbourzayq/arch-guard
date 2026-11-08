# Proposal and validation interfaces

Paths below are examples; substitute the verified input, policy and build contexts.
CLI paths resolve from its working directory. MCP paths are relative to the
operator's `Workspace:Root` and cannot escape it. The policy directory must contain
the full loaded project/source scope. Keep proposed and adopted policies at the
same root so selectors and imports keep their meaning.

## Capability-aware discovery

Check installed `archguard init --help` for `--discover`. If absent or command/tool
access is unavailable, report discovery unavailable and use manual authoring below.
Do not send unsupported commands or invent an MCP discovery method.

When discovery is supported, `archguard init --discover Shop.slnx` without trust
only reads literal paths. It produces an incomplete, unevaluated JSON report and
exit `2`, with no YAML. It still creates an output, so choose a fresh output location
or deliberately archive the prior report before another run.

With build authorization, restore and evaluate each explicit context:

```sh
dotnet restore Shop.slnx -p:Configuration=Release
archguard init --discover Shop.slnx --configuration Release --trusted
```

For a selected target add `-p:TargetFramework=net10.0` to restore and
`--framework net10.0` to CLI evaluation. Multi-targeted discovery requires an explicit
framework. Full scans/discovery do not restore automatically. Re-restore after
changes to build/project/package inputs. Preserve reports for each context.

Discovery creates `architecture.guardian.discovery.json` and, only with complete
inventory and resolved candidate ownership, `architecture.guardian.proposed.yaml`
beside the input. An optional `--output-directory <existing-directory>` must contain
the full scope; selectors are relative to that directory. Existing outputs are
create-only. Never delete/replace an approved policy or baseline to retry discovery.
Read `status`, `exploredContexts`, `observations`, `candidateOwnership`, `inferences`,
`unresolvedQuestions`, `configurationValidation` and `compliance` separately. Declared but
unexplored frameworks remain uncovered. High-confidence style suggestions still
require review. Runtime/reflection edges and namespace-only slices are limitations.

## Manual schema-v1 candidate

For three verified projects at the exact paths shown, this is a complete candidate.
Adapt every selector to the actual inventory, including tests and referenced projects.
It is a layer-boundary example, not a default architecture for every repository.

```yaml
version: 1
modules:
  - id: Shop
    projects: ["src/Shop.Domain/Shop.Domain.csproj", "src/Shop.Application/Shop.Application.csproj", "src/Shop.Infrastructure/Shop.Infrastructure.csproj"]
layers:
  - id: Domain
    projects: ["src/Shop.Domain/Shop.Domain.csproj"]
  - id: Application
    projects: ["src/Shop.Application/Shop.Application.csproj"]
    allowedDependencies: [Domain]
  - id: Infrastructure
    projects: ["src/Shop.Infrastructure/Shop.Infrastructure.csproj"]
    allowedDependencies: [Domain, Application]
rules:
  AG001: error
  AG002: error
  AG003: error
  AG004: error
  AG005: error
  AG006: error
gate:
  failOn: error
```

Every loaded project must match exactly one module and one layer; each selector
must match. Contract declarations select whole projects inside their owning module,
for example `contracts: [{ id: api, projects: ["src/Billing.Contracts/*.csproj"] }]`.
An outgoing module permission is `contract:Billing/api` or `module:Billing`;
layer permissions use layer IDs. Permissions are direct, case-sensitive and not
transitive. Same-module/same-layer access is allowed by the respective checks.
AG004 protects cross-module implementation access even without contract declarations;
declared contracts identify the public surfaces it allows. AG005 needs explicit
forbidden namespaces/assemblies and AG006 needs explicit layer `domainInfrastructure`
restrictions; selecting all six rules alone does not ban technologies. Agree those
intentions rather than inferring permission from observed accesses.

Use the installed version's policy reference/schema when available. Strict loader
validation plus resolved inventory inspection below checks supported YAML without
requiring an additional YAML validator in a copied skill. Do not invent a new format.

## CLI candidate and adopted validation

```sh
archguard inspect Shop.slnx --policy architecture.guardian.proposed.yaml --configuration Release --trusted
archguard scan Shop.slnx --policy architecture.guardian.proposed.yaml --configuration Release --trusted --format json
```

Add `--framework <tfm>` consistently when selected. `inspect` resolves configuration
and inventory without evaluating rules. `scan` exit `0` is completed/passed, `1` is
completed/failed gate and `2` is unresolved (invalid, incomplete, failed or cancelled).
Read JSON `status`, `gateOutcome`, `issues`, `rules` and diagnostics; an unevaluated
gate is `notEvaluated`. A passed gate can still include warnings below its threshold.
Use fresh output paths if saving reports. `archguard explain AG002` explains rule
metadata; instance evidence comes from the actual scan, not a fabricated ID.

After explicit adoption approval, repeat both commands with
`--policy architecture.guardian.yaml`. Do not assume proposal validation covers a
different adopted file, scope or build context.

## MCP candidate and adopted validation

Discover supported tools first. The current read-only tools are `list_rules`,
`get_architecture`, `analyze_solution`, `explain_violation`, `analyze_changes`.
They cannot restore, write proposals, activate policy or persist baselines.
Restore separately after build authorization. The operator enables trust; an agent
cannot change it by passing a tool parameter.

1. Call `list_rules` with `{"policyPath":"architecture.guardian.proposed.yaml"}`.
2. Call `get_architecture`, then `analyze_solution` with
   `{"inputPath":"Shop.slnx","policyPath":"architecture.guardian.proposed.yaml","configuration":"Release"}`.
   Add `"targetFramework":"net10.0"` when selected.
3. Require `status: "completed"` before interpreting `gateOutcome` (`passed`,
   `failed`, `not-evaluated`). Preserve operational `issues` and coverage limits.
4. For actual findings, call `explain_violation` with the same context and an exact
   fresh returned `diagnosticId`. Examine evidence, dependency paths, constraints
   and candidate contracts' real API. A stale/unknown ID needs a fresh scan.
5. After adoption approval and file reconciliation through ordinary tools, repeat
   list/inspect/scan calls using the adopted `policyPath` in all required contexts.

## Deliberate incremental adoption

- **Full enforcement:** scan without `--baseline`/`--base`, gate all selected findings
  at the reviewed threshold. Correct authorized violations and revalidate.
- **Reviewed baseline:** first retain a full scan and review its debt. Only after
  explicit debt-acceptance approval use `archguard baseline update Shop.slnx --policy
  architecture.guardian.yaml --baseline accepted.json --configuration Release
  --trusted`. This can replace an existing baseline: review its exact delta first.
  Subsequent CLI scans add `--baseline accepted.json`. Existing errors remain visible
  and can pass; use separate baselines for separately gated contexts. MCP does not
  read/write accepted-debt JSON files; use CLI or report this choice unavailable.
- **Git-base gate:** CLI scan adds `--base <explicit-ref>`; MCP `analyze_changes`
  adds `baseReference` and `scope: "solution"` to the same context. Both complete
  snapshots must contain input/policy at the same repository-relative paths. A base
  predating first policy adoption cannot validate that comparison; choose an
  explicitly reviewed later base or another mechanism. Prepare both SDK/package
  contexts beforehand; snapshot preparation uses cache-only restore and does not
  fetch history. Trust includes build logic in both trees. Missing history/assets
  stays unresolved. Existing debt may pass this gate; report the separate full scan.

CLI `--baseline` and `--base` are mutually exclusive. None of these mechanisms
licenses automatic debt acceptance, omitted projects or weaker rules. Keep normal
build/tests and an independent CI gate; propose CI changes separately for approval.
