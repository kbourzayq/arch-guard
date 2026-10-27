# Getting started

Install the [published CLI](../README.md#install-the-cli), then use this guide from
**your repository root** to define policy and run your first architecture gate.
A source checkout is needed only for the optional reference example below.

## Check your own repository

### 1. Define the analysis scope

Choose a `.sln`, `.slnx` or C# `.csproj` input. Project inputs also load their
referenced projects. Place `architecture.guardian.yaml` in a directory containing
the input and every project in that scope, usually your repository root.

Every loaded project must match exactly one module and one layer. Referenced
projects and test projects need ownership too. Selectors are relative to the policy
directory and use `/` on every operating system. Unmatched projects, overlapping
ownership and selectors matching nothing are configuration errors.

### 2. Write a policy

For a repository with this layout:

```text
Shop.slnx
architecture.guardian.yaml
src/
  Shop.Domain/Shop.Domain.csproj
  Shop.Application/Shop.Application.csproj
  Shop.Infrastructure/Shop.Infrastructure.csproj
```

Start with:

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
  AG003: error
gate:
  failOn: error
```

Adapt the selectors to your real inventory. This policy permits Application → Domain
and Infrastructure → Application/Domain. It forbids the reverse cross-layer
dependencies. Same-layer dependencies are allowed. Only listed rules run; declaring
constraints does not implicitly enable a rule.

As the application grows, add [modules and public contracts](policy.md#dependency-permissions)
and enable the corresponding [rules](rule-catalog.md). The
[policy reference](policy.md) describes strict validation and editor schema setup.

### 3. Restore, inspect and scan

From **your repository root**, authorize its build logic before restoring or
loading projects. Replace `Shop.slnx` with your solution name:

```sh
dotnet restore Shop.slnx -p:Configuration=Release
archguard inspect Shop.slnx --policy architecture.guardian.yaml --configuration Release --trusted
archguard scan Shop.slnx --policy architecture.guardian.yaml --configuration Release --trusted
```

`inspect` lets you verify ownership and permissions before evaluating rules.
`scan` applies selected rules and evaluates the severity gate. For multi-targeted
inputs, restore the relevant context and add `--framework <tfm>` to inspect/scan;
repeat for each framework and configuration that your gate must cover.

### 4. Act on the result

Fix a forbidden dependency by moving the responsibility, depending on an allowed
contract, or inverting the dependency. Change policy when the intended architecture
changes. `explain AG002` prints the rule's title and documentation URL without
loading a workspace.

| Exit | What to do |
| --- | --- |
| `0` | The requested context completed and passed its severity gate. |
| `1` | Review the findings; the context completed but violated the gate. |
| `2` | Resolve the reported input or coverage problem and rerun before drawing a compliance conclusion. |

Missing SDKs, failed restore, compiler errors and incomplete extraction cannot
produce a passing scan. See [troubleshooting](troubleshooting.md).

## Add a CI gate

Provision the SDKs required by both the tool and your target repository and
install the [CLI package](../README.md#install-the-cli). Restore the target in
the intended configuration, then invoke `archguard` as a normal pipeline step:

```sh
dotnet restore Shop.slnx -p:Configuration=Release
archguard scan Shop.slnx --policy architecture.guardian.yaml --configuration Release --trusted
```

Run this in the target repository root and let the pipeline fail on **either**
nonzero exit code. Do not turn exit `2` into a successful gate.
Use an execution environment whose build logic you authorize; `--trusted` is not
an isolation mechanism. Analysis does not restore packages for you.

For coding-agent access, continue with [MCP setup](mcp.md). For per-compilation
feedback, use the [compiler analyzer](compiler-analyzer.md) alongside the solution gate.

## Run the included example

For a local demonstration, obtain a [development checkout](installation.md#install-from-source)
and run the commands below from its root with the installed `archguard`. A source
build can substitute `dotnet artifacts/cli/ArchitectureGuardian.Cli.dll` for that
command after publishing. This checkout requires its pinned SDK.

The Clean Architecture example contains Domain, Application, Infrastructure and
Host projects with a committed policy. Restore the same configuration you will scan:

```sh
dotnet restore tests/Fixtures/ReferenceSolutions/CleanArchitecture/CleanArchitecture.slnx -p:Configuration=Compliant
archguard scan tests/Fixtures/ReferenceSolutions/CleanArchitecture/CleanArchitecture.slnx --policy tests/Fixtures/ReferenceSolutions/CleanArchitecture/architecture.guardian.yaml --configuration Compliant --trusted
```

Expect a completed scan with zero diagnostics, `Gate passed` and exit code `0`.
The `--trusted` flag authorizes MSBuild execution for this repository-owned example.
For other inputs, make that trust decision before restoring or loading their projects.

Now select a committed configuration that makes Application depend on Infrastructure:

```sh
dotnet restore tests/Fixtures/ReferenceSolutions/CleanArchitecture/CleanArchitecture.slnx -p:Configuration=AG002
archguard scan tests/Fixtures/ReferenceSolutions/CleanArchitecture/CleanArchitecture.slnx --policy tests/Fixtures/ReferenceSolutions/CleanArchitecture/architecture.guardian.yaml --configuration AG002 --trusted
```

Expect one AG002 finding, source evidence, remediation, `Gate failed` and exit
code `1`. The input still compiles; the architecture policy rejects its dependency.
For module permissions and cycles, see the [modular-monolith examples](m1-demo.md).
