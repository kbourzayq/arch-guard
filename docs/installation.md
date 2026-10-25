# Installation

Architecture Guardian currently runs from source. These instructions produce local
CLI and MCP executables; they do not require a globally installed `archguard` command
or assume that a package has been published to nuget.org.

## Requirements

| Requirement | Details |
| --- | --- |
| Operating system | Linux, macOS or Windows; all three run the repository's CI checks. |
| SDK for this checkout | Exactly .NET SDK `10.0.402`, as pinned by `global.json`. Roll-forward is disabled and preview SDKs are excluded. |
| Runtime | CLI and MCP target .NET 10. The SDK above includes the runtime needed to run the published DLLs. |
| Git | Install [Git](https://git-scm.com/downloads/) to clone the repository. |
| Package access | Access to the configured NuGet feeds for initial restore, or an already populated package cache. |
| Target repository | Install the SDK selected by its `global.json`, restore the input, and authorize its build logic before workspace analysis. |

Download the SDK for your operating system and CPU from the official
[.NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
Select the exact SDK version pinned in `global.json`. Installing another patch or
feature band does not satisfy this checkout's `disable` roll-forward policy.
SDK patch updates require manually changing the version in `global.json`.

Check your installation with `dotnet --list-sdks`. From this checkout, `dotnet --version`
should select the version allowed by `global.json`. A .NET 10 runtime alone is
insufficient for source builds and MSBuild workspace loading.

## Install from source

Run from a terminal. The Git and .NET commands below also work in PowerShell:

```sh
git clone https://github.com/kbourzayq/arch-guard.git
cd arch-guard
dotnet restore ArchitectureGuardian.slnx --locked-mode
dotnet publish src/ArchitectureGuardian.Cli --configuration Release --no-restore --output artifacts/cli
dotnet publish src/ArchitectureGuardian.Mcp --configuration Release --no-restore --output artifacts/mcp
```

`artifacts/cli` and `artifacts/mcp` are local installation directories inside this
checkout. If you move an installation, copy its **entire directory**, including
dependencies and build-host support files. The published output is framework-dependent;
the destination still needs .NET 10 and the SDK required for workspace loading.
Keep generated artifacts out of commits.

Verify the CLI:

```sh
dotnet artifacts/cli/ArchitectureGuardian.Cli.dll --version
dotnet artifacts/cli/ArchitectureGuardian.Cli.dll --help
dotnet artifacts/cli/ArchitectureGuardian.Cli.dll explain AG001
```

These relative paths assume your terminal is in the checkout root. When invoking
the CLI from another directory, pass the full path to its DLL. Input and policy
arguments resolve from your terminal's working directory. Quote paths containing spaces.

Continue with [getting started](getting-started.md) for a first scan, or
[MCP client setup](mcp.md#configure-your-client) to use the server in a coding agent.
The MCP executable is a stdio service: connect through an MCP client rather than
expecting a web dashboard or standalone command prompt.

## Use the source checkout directly

For development, you can run the CLI without publishing it:

```sh
dotnet run --project src/ArchitectureGuardian.Cli -- --help
dotnet run --project src/ArchitectureGuardian.Cli -- explain AG001
```

The `--` separates `dotnet run` options from Architecture Guardian arguments.
For MCP client configuration, use the published DLL; build output from `dotnet run`
can interfere with protocol stdout.

## Build a local compiler analyzer package

From the checkout root after locked restore:

```sh
dotnet pack src/ArchitectureGuardian.Analyzers --configuration Release --no-restore --output artifacts/analyzers
```

This produces `ArchitectureGuardian.Analyzers.0.1.0.nupkg` with the current source
version. Use `artifacts/analyzers` as a local package source in the consuming
repository. The analyzer also needs an XML ownership policy; installing the package
alone does not configure architecture checks. Follow the complete
[analyzer setup](compiler-analyzer.md#install-and-distribute-policy).

## Update or remove a local installation

To update, choose the revision you want to use, restore with `--locked-mode`, and
repeat the publish commands. Stop clients using the MCP executable before replacing
its files. Keep the server and CLI at the same revision when comparing their results.

To remove an installation, remove its MCP client entry and any local wrappers or
PATH entries you created, then delete the published directories. For the compiler
integration, remove the package and its AdditionalFiles entry from participating
projects when you no longer need the checks.

See [troubleshooting](troubleshooting.md) if SDK selection, restore or startup fails.
