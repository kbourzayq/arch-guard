# Troubleshooting

Start with the command's stderr or the MCP response's `status` and `issues`.
An empty diagnostic list alone does not establish complete analysis.

## Installation and command startup

**The `archguard` command is missing.** Install the [published CLI](../README.md#install-the-cli).
For global tools, add `$HOME/.dotnet/tools` (macOS/Linux) or
`%USERPROFILE%\.dotnet\tools` (Windows) to PATH and restart the terminal/agent.
Check `dotnet tool list --global`. For a private tool directory, invoke its full
path or `./.tools/architecture-guardian/archguard` from the target repository root.
A source-development publish is a separate option in [local development](installation.md).

**The requested runtime is unavailable.** CLI/MCP target .NET 10. Check
`dotnet --list-runtimes` and install .NET 10 for your CPU architecture. An installed
older target SDK alone may not provide the runtime required by the tools.

**The SDK requested by `global.json` is unavailable.** Check `dotnet --list-sdks`
and install a version permitted by that file. This development checkout pins SDK
`10.0.402` with roll-forward disabled. Consumer repositories may select a different
SDK; they do not need this checkout's exact version just to use the packages.
A runtime-only installation cannot evaluate MSBuild projects.

**NuGet cannot find the package.** Check configured sources and package-source
mapping; the three `ArchitectureGuardian.*` distribution packages are available
on nuget.org at the version shown in the [README](../README.md). A private-only
NuGet configuration may need the public feed enabled or an approved mirror.
Do not replace existing private feeds with a public-only configuration.

**A moved executable fails to start.** Keep an entire published directory or
installed tool directory, including dependencies and hidden tool-store files.
For desktop MCP clients, PATH can differ from your terminal: configure the absolute
installed executable path. See [MCP setup](mcp.md#configure-your-client).

## Restore and workspace problems

### Incomplete restore or missing context

**`restore-required`, unavailable references or compiler errors.** Restore the
target input explicitly, using the configuration/framework you intend to analyze.
Resolve failed restores and compilation problems before expecting full coverage.
The loader does not run restore and does not guarantee assets remain fresh after edits.

For a trusted input, check restore and build in the same context before rescanning
(replace the paths/framework with those declared by your repository):

```sh
dotnet restore path/to/Application.slnx -p:Configuration=Release -p:TargetFramework=net10.0
dotnet build path/to/Application.slnx --configuration Release --framework net10.0 --no-restore
archguard load path/to/Application.slnx --trusted --configuration Release --framework net10.0
archguard scan path/to/Application.slnx --policy path/to/architecture.guardian.yaml --trusted --configuration Release --framework net10.0
```

Resolve unavailable package feeds, credentials, SDK/reference packs and broken
project/assembly references first. Check that referenced projects are included in
the intended input and mapped by the policy. An unused missing reference can still
prevent completion. Existing `obj/project.assets.json` alone does not prove that
restore succeeded for the current project and context. Keep workspace warnings and
compiler errors visible; partial findings plus exit `2` are not a gate pass.

**`workspace-untrusted`.** CLI workspace commands require `--trusted`. MCP builds
require the operator to enable `Workspace:Trusted`; tool arguments cannot grant it.
Restore and MSBuild evaluation can execute repository build logic. Trust and
[isolation](workspace-loading.md#trust-and-isolation) are separate decisions.

### Multi-targeting and configurations

**`framework-required` or `framework-unavailable`.** Specify `--framework` in CLI
or `targetFramework` in MCP. Every loaded project must declare that framework.
Use separate inputs/runs when frameworks differ across projects.

For `<TargetFrameworks>net10.0;netstandard2.1</TargetFrameworks>`, restore and scan
each declared framework explicitly. Repeat for relevant Debug/Release configurations:
conditional references, source files and preprocessor branches can change findings.
A successful run covers only its selected context. Use a separate baseline per
independently gated context; sharing one inventory can misclassify context-specific
findings as resolved. See [framework selection](workspace-loading.md#configuration-frameworks-and-sdks).

If direct multi-target `.csproj` loading reports unmatched project/metadata reference
warnings, try the explicit solution containing its referenced projects, still with
`--framework` selected and complete policy mappings. This is a documented workspace
limitation, not permission to suppress warnings; see the
[validated context matrix](semantic-accuracy.md#shared-cases-and-intentional-differences).

**A later MCP request needs another SDK.** MSBuild registration is process-wide.
Start another server process for a repository that requires a different SDK.

### Generated code and source generators

**A finding points into `.g.cs` or `generated/...`.** Both engines include generated
source supplied by Roslyn. Generated-code attributes and file naming do not exempt
bound references from the rules. Solution evidence uses portable logical paths
such as `generated/PROJECT/GENERATED_FILE_ID/HINT`; these are identities, not promises
that a file exists there. Compiler locations use the host's paths. Find the generator
and hint through the project/IDE, then change the generator, its templates/options
or input model and regenerate. Hand edits to generated output will be overwritten.
See [evidence paths](dependency-graph.md#stable-identities-and-ordering).

**`source-generator-error`, `source-generator-failed` or missing generated types.**
Inspect the named project, generator and underlying diagnostics. Restore generator
dependencies, supply its AdditionalFiles/options, and check SDK/compiler compatibility
in the selected context. Generators can run during workspace compilation and again
in its validation pass; make their inputs deterministic. Generator errors/exceptions
prevent completed coverage even when some other symbols bind. Ordinary generator
warnings alone do not block completion. See
[generator validation](workspace-loading.md#roslyn-and-msbuild-responsibilities).

**Compiler and CLI counts differ.** Solution rules aggregate evidence; compiler
diagnostics locate bound explicit names and cover a narrower semantic surface.
Compare the rule, ownership, restriction and bound use rather than raw counts or
diagnostic IDs. Check the XML ownership snapshot as well as YAML; there is no
automatic exporter. See [engine parity](semantic-accuracy.md).

## Policy and gate problems

**Unmatched or overlapping project mappings.** Paths are relative to the policy
directory, not your shell or MCP workspace root. Every loaded project, including
referenced projects, needs exactly one module and layer. Each selector must match
at least one project. Use `inspect` to review the resolved inventory.

**Warnings do not fail the scan.** Check `gate.failOn`. `error` permits warnings;
`warning` fails on warnings and errors; `info` fails on any finding. Only selected
rules run. See [policy selection and gates](policy.md#rule-selection-severity-and-gates).

**Exit code `2` with zero findings.** Analysis did not complete reliably. Resolve
the operational issues and rerun. Do not suppress the exit code to make CI pass.

**A baseline hides errors or fails to match moved code.** Baseline mode gates only
new findings, while retaining existing errors in output. Project/module/symbol or
constraint changes can create new identities; line movement alone can preserve
semantic fingerprints. Review the [matching contract](baselines.md#version-one-identity-and-matching).
Use `baseline update` only to accept reviewed debt after completed analysis, never
to bypass a loading problem. `--base` and `--baseline` cannot be combined.

## Offline operation and repeatability

**A scan attempts network access or changes between runs.** The tool does not fetch
rules or help pages and full scans do not restore. Repository-controlled MSBuild
tasks, SDK resolvers and source generators can still access the network, read local
state or change files. Preinstall SDKs/packs, explicitly restore dependencies, fix
the build context and generated inputs, then enforce network isolation outside the
tool. Deterministic rule evaluation cannot make nondeterministic build logic stable.
See [offline preparation](workspace-loading.md#offline-and-deterministic-operation).

**Can `--trusted` safely inspect an unfamiliar repository?** It authorizes execution
of that repository's build logic. Run restore and analysis in a disposable environment
without credentials or privileged host mounts/services, with resource and time limits.
MCP's workspace root and read-only annotations do not sandbox that execution.
Follow [trust and isolation](workspace-loading.md#trust-and-isolation).

## MCP connection and path problems

**The client cannot start the server.** Check that `ArchitectureGuardian.Mcp` is
installed and configure the absolute `archguard-mcp` executable path
(`archguard-mcp.exe` on Windows). Confirm .NET 10 and the required SDK are available
to the client's process, whose PATH may differ from your terminal. Provide an
existing absolute `Workspace:Root` and replace all template markers. Do not rely
on shell expansion or the client's working directory. Use single-quoted TOML paths
to preserve Windows backslashes; JSON needs forward slashes or escaped backslashes.
For a source-development installation, publish first and configure `dotnet` plus
the absolute published DLL path, keeping its complete directory.

**The client reports invalid protocol output.** Launch the installed server
executable. For a source build, launch its published DLL with `dotnet` rather than
`dotnet run`, which can write build output to stdout. The server reserves stdout
for MCP; inspect stderr for startup diagnostics. See
[client configuration](mcp.md#configure-your-client).

**An explicit path is rejected.** Tool inputs must exist inside the configured root
and must not traverse descendant symbolic links/reparse points. Widen the
operator-selected root only when that is the intended workspace. This root check
does not sandbox trusted builds or their imports.

**The response exceeds 65536 bytes.** The tool returns an error instead of truncating
findings. Use supported metadata filters for `get_architecture`/`list_rules`, or a
smaller complete input/policy scope for `analyze_solution`. Do not interpret the
error as a clean scan. The [MCP reference](mcp.md) lists all limits.

**Change comparison cannot load its base or snapshot.** Fetch the explicit base
(and required history in a shallow clone), install the SDK required by each tree,
and populate the package cache through an explicit restore. Comparison uses
cache-only restore in temporary snapshots and never fetches Git history or downloads
packages. Confirm that the input and policy exist at the same relative paths in
both trees and that MCP `Workspace:Root` contains the Git root. See [change analysis](changes.md).

## Known analysis limitations

- Solution analysis models static context. It does not infer dependencies through
  reflection, runtime dependency injection or concrete virtual dispatch targets.
- Coverage applies to the selected configuration and framework. Other build contexts
  require separate runs.
- The repository's own solution currently exposes extraction gaps around existing
  `notnull` constraints and some target-typed/discard constructs, along with duplicate
  analyzer release-file workspace warnings. Its self-scan can return incomplete
  coverage even when build/tests pass. These findings do not establish a passing gate.
- Compiler analysis checks a narrower bound-name surface and requires an explicit
  XML ownership snapshot. It does not discover missing referenced-assembly mappings
  or check whole-solution cycles. IDE UI behavior is not validated by this repository.
- JSON v1 and SARIF 2.1.0 exports are available for CLI scans; see [reports](reports.md).
  Git comparison uses isolated full snapshots; see
  [change analysis](changes.md) for supported inputs and offline preparation. CLI
  [baselines](baselines.md) require explicit files and never relax coverage failures.

See [semantic accuracy](semantic-accuracy.md) for the rule-by-rule scope and
[contributing](../CONTRIBUTING.md#report-a-bug) for a useful issue report.
