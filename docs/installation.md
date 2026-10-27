# Installation

Architecture Guardian distributes the `archguard` CLI, `archguard-mcp` stdio server
and compiler analyzer as three NuGet packages. Build and install packages locally
using the steps below, or select a version already published to your package feed.
These instructions do not assume that a version is available on nuget.org.

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

## Install the .NET tools from local packages

From this checkout, with Python 3.10+, the pinned SDK and Python Commitizen installed
on PATH (see [release tooling](releases.md#build-and-verify-packages)):

```sh
python3 eng/packages.py pack
dotnet tool install ArchitectureGuardian.Cli --tool-path artifacts/tools --version 0.1.0 --configfile NuGet.Config --add-source artifacts/packages
dotnet tool install ArchitectureGuardian.Mcp --tool-path artifacts/tools --version 0.1.0 --configfile NuGet.Config --add-source artifacts/packages
artifacts/tools/archguard --version
artifacts/tools/archguard --help
artifacts/tools/archguard explain AG001
```

`pack` uses locked restore, creates all three packages in `artifacts/packages`,
checks their contents and metadata, rebuilds and compares their payloads, then
writes `SHA256SUMS`. Use an empty output directory; choose another with `--directory`
for subsequent runs. Run `python3 eng/packages.py smoke` to verify isolated local-feed
tool installation, real CLI/MCP workspace scans and analyzer consumer builds.

The two tool packages contain their application dependencies and Roslyn build-host
files. Install .NET 10 and the target repository's compatible SDK separately.
The compiler analyzer carries neither those dependencies nor a .NET 10 requirement;
see [host compatibility](compiler-analyzer.md#host-compatibility-and-validation).

The installed commands above work from the checkout root; on Windows the shims are
`archguard.exe` and `archguard-mcp.exe`. Use the absolute executable path from other
directories or in MCP client configuration. For the installed MCP tool, use
`command` set to that full `archguard-mcp` path and pass only workspace options;
see the [packaged server example](mcp.md#install-the-packaged-server).

To install a version available on nuget.org globally instead:

```sh
dotnet tool install --global ArchitectureGuardian.Cli --version 0.1.0
dotnet tool install --global ArchitectureGuardian.Mcp --version 0.1.0
archguard --help
```

Ensure the .NET global tool directory is on your terminal/client's PATH. Pin the
same version for CLI, MCP and analyzers when comparing results. Package versions,
compiler hosts and release prerequisites are defined in the [release policy](releases.md).

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

For tool installations, use `dotnet tool update` with the same package ID,
`--tool-path artifacts/tools` (or `--global`), source and desired `--version`.
Stop MCP clients first. Remove each tool with `dotnet tool uninstall
ArchitectureGuardian.Cli --tool-path artifacts/tools` or the corresponding MCP
package ID; use `--global` for a global installation. Remove the MCP client entry
when uninstalling its server.

See [troubleshooting](troubleshooting.md) if SDK selection, restore or startup fails.
