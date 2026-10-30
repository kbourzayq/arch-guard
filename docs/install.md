# Installation

Install the integration that fits your workflow, then share YAML across the tools
you use. No source checkout or build of Architecture Guardian is required.

## Requirements

| Integration | Requirements |
| --- | --- |
| CLI and MCP | Linux, macOS or Windows; .NET 10 runtime; an installed SDK compatible with the repository being analyzed |
| Compiler analyzer | A Roslyn 4.8+ compiler host supporting .NET Standard 2.0; no standalone .NET 10 runtime requirement |
| Agent skill | A compatible local agent with MCP access or permission to run the CLI |

Install the [.NET SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0),
which includes the runtime, and any SDK selected by your target repository's
`global.json`. A runtime alone cannot load MSBuild projects. Check installed SDKs
with `dotnet --list-sdks` and runtimes with `dotnet --list-runtimes`.
The exact SDK pinned in the Architecture Guardian source repository is required only for
source development; it is not a prerequisite for using the packages.

Install a released version from NuGet. The examples below target `0.3.0`:

| Package | What you install |
| --- | --- |
| [ArchitectureGuardian.Cli](https://www.nuget.org/packages/ArchitectureGuardian.Cli) | `archguard` command |
| [ArchitectureGuardian.Mcp](https://www.nuget.org/packages/ArchitectureGuardian.Mcp) | `archguard-mcp` stdio server |
| [ArchitectureGuardian.Analyzers](https://www.nuget.org/packages/ArchitectureGuardian.Analyzers) | C# compiler analyzer |

Keep package versions aligned when comparing integrations. See
[compatibility and releases](releases.md).

## Install the CLI

Run installation and updates from a directory where `dotnet --version` selects
.NET SDK 10. A repository's `global.json` can select an older or unavailable SDK
even when .NET 10 is installed. In that case, change to a directory outside that
repository before installing either CLI or MCP; keep the repository's SDK selection
for its own restore and analysis. No Architecture Guardian checkout is needed:

```sh
dotnet tool install --global ArchitectureGuardian.Cli --version 0.3.0
archguard --version
archguard --help
archguard explain AG001
```

The version starts with `0.3.0`; help lists `scan`, `inspect`, `load`, `explain`
and `baseline`. Ensure the global tool directory is on PATH: `$HOME/.dotnet/tools`
on macOS/Linux or `%USERPROFILE%\.dotnet\tools` on Windows. Restart your terminal
or agent after changing PATH. See [troubleshooting](troubleshooting.md#installation-and-command-startup).

### Scan your repository

From **your repository root**, create `architecture.guardian.yaml` using the
[getting-started policy](getting-started.md#check-your-own-repository).
Replace `Shop.slnx` below with your `.sln`, `.slnx` or C# `.csproj` input:

```sh
dotnet restore Shop.slnx -p:Configuration=Release
archguard inspect Shop.slnx --policy architecture.guardian.yaml --configuration Release --trusted
archguard scan Shop.slnx --policy architecture.guardian.yaml --configuration Release --trusted
```

Authorize the repository's build logic **before restoring or scanning**: MSBuild
and generators can execute code. `--trusted` records that authorization for the
scan; it does not provide isolation. Scans do not restore automatically.
For multi-targeted projects, select `--framework <tfm>` and prepare that context.

| Exit code | Result |
| --- | --- |
| `0` | Analysis completed and the configured severity gate passed. |
| `1` | Analysis completed and the gate failed. |
| `2` | Invalid input, incomplete coverage, failure or cancellation; compliance is unresolved. |

A completed scan can pass with warnings below the configured threshold. Zero
findings in an incomplete scan never mean success. See [CLI reference](cli.md),
[JSON/SARIF reports](reports.md), [baselines](baselines.md) and
[Git change analysis](changes.md).

## Install the MCP server

```sh
dotnet tool install --global ArchitectureGuardian.Mcp --version 0.3.0
```

Find the installed executable with `command -v archguard-mcp` on macOS/Linux or
`(Get-Command archguard-mcp).Source` in PowerShell. Configure your client with that
**absolute path** and the absolute root of the repository to analyze. The server
communicates over stdio and is launched by the client.

For Codex, merge this into your local `~/.codex/config.toml`, replacing both markers:

```toml
[mcp_servers.architecture-guardian]
command = '<FULL_PATH_TO_ARCHGUARD_MCP>'
args = ["--Workspace:Root", '<FULL_PATH_TO_YOUR_REPOSITORY>', "--Workspace:Trusted", "false"]
tool_timeout_sec = 180
```

Single-quoted TOML paths preserve Windows backslashes. If a path contains an
apostrophe, use a double-quoted string with forward slashes instead.

Start with trust disabled and call `list_rules` to read policy. After authorizing
and restoring the target repository, set the final argument to `"true"` and reconnect
for workspace analysis. Verify connection and tool discovery through your client;
registration alone does not prove the server started.

The server exposes five read-only tools: `list_rules`, `get_architecture`,
`analyze_solution`, `explain_violation` and `analyze_changes`. Check `status` and
`gateOutcome` in analysis responses. Read-only tools can still evaluate trusted
MSBuild logic. See [MCP setup](mcp.md#configure-your-client) for Codex,
Claude Code and Copilot CLI configuration, requests and troubleshooting.

## Install the compiler analyzer

In your consuming repository, merge this into a root `Directory.Build.props`:

```xml
<Project>
  <ItemGroup>
    <PackageReference Include="ArchitectureGuardian.Analyzers" Version="0.3.0" PrivateAssets="all" />
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)architecture.guardian.yaml" />
  </ItemGroup>
</Project>
```

For central package management, put the version in `Directory.Packages.props` and
omit `Version` on `PackageReference`. `PrivateAssets="all"` keeps the analyzer from
flowing to consumers of your own packages.

Use the same `architecture.guardian.yaml` as CLI and MCP. The analyzer package's
MSBuild targets supply evaluated project/assembly ownership metadata automatically.
YAML selects AG001, AG002 and AG004–AG006 and supplies their default severities;
normal `.editorconfig` settings can override severity or suppress selected rules.
AG003 cycle analysis and `gate.failOn` remain solution-scan responsibilities.

Then run `dotnet restore Shop.slnx` and `dotnet build Shop.slnx --no-restore`,
substituting your solution. Missing or invalid configuration produces AG000.
Retain a CLI solution scan in CI for complete repository coverage.

This release supports shared YAML. Earlier XML-only packages require the
[legacy XML setup](compiler-analyzer.md#compiler-policy-snapshot).
The first minor release with YAML support retains XML for one transition release;
its removal is planned for the following minor release. See the
[complete analyzer guide](compiler-analyzer.md) for migration and host limits.

## Install the optional agent skill

The portable [architecture-guardian skill](../skills/architecture-guardian/SKILL.md)
guides inspect → implement → validate → correct → report using MCP or CLI. It keeps
architecture constraints in YAML and requires fresh validation after corrections.

Copy the complete `skills/architecture-guardian` folder from this repository into
your target repository's supported skill directory:

| Agent | Project destination |
| --- | --- |
| Codex | `.agents/skills/architecture-guardian/` |
| Claude Code | `.claude/skills/architecture-guardian/` |
| Copilot CLI | `.github/skills/architecture-guardian/` |

The skill is distributed as repository files, separately from the NuGet packages.
Use a repository revision containing `skills/architecture-guardian`; the original
package release predates the skill. See [skill installation and invocation](agent-workflow.md)
for copy commands, discovery, `AGENTS.md` integration and an end-to-end example.
The skill complements your normal build, tests and independent CI gate.

## Install tools only for one repository

Instead of `--global`, install into a private directory. Run installation from a
directory selecting SDK 10, and replace the marker below with your target
repository's **absolute path**. Add `.tools/architecture-guardian/` to that
repository's `.gitignore`:

```sh
dotnet tool install ArchitectureGuardian.Cli --tool-path "<FULL_PATH_TO_YOUR_REPOSITORY>/.tools/architecture-guardian" --version 0.3.0
dotnet tool install ArchitectureGuardian.Mcp --tool-path "<FULL_PATH_TO_YOUR_REPOSITORY>/.tools/architecture-guardian" --version 0.3.0
```

Then return to the target repository root. Use `./.tools/architecture-guardian/archguard` in place of `archguard` in scan
commands (`archguard.exe` on Windows). For MCP, resolve the full path to the
`archguard-mcp` shim in that directory. Keep the complete tool directory, including
its hidden package store.

## Update or remove

Stop connected MCP clients before updating or removing their server. Run tool
updates from a directory selecting SDK 10, as for installation. To select a
published version, replace the version below consistently across tools and analyzers:

```sh
dotnet tool update --global ArchitectureGuardian.Cli --version 0.3.0
dotnet tool update --global ArchitectureGuardian.Mcp --version 0.3.0
```

Update the analyzer's package version in your project or central package file and
restore. For repository-local tools, replace `--global` with
`--tool-path "<FULL_PATH_TO_YOUR_REPOSITORY>/.tools/architecture-guardian"`, using
the repository's absolute path. Reconnect MCP after an update.

To uninstall global tools:

```sh
dotnet tool uninstall --global ArchitectureGuardian.Cli
dotnet tool uninstall --global ArchitectureGuardian.Mcp
```

Use the same tool-path substitution for local tools. Remove the MCP client entry.
To remove compiler integration, remove its package reference and AdditionalFiles
entry, then restore; remove shared policy/severity settings only when no remaining
project uses them. Remove an installed skill by deleting its copied folder and
adjusting instructions that reference it.
