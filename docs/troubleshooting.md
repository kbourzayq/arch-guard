# Troubleshooting

Start with the command's stderr or the MCP response's `status` and `issues`.
An empty diagnostic list alone does not establish complete analysis.

## SDK or installation problems

**The SDK requested by `global.json` is unavailable.** Check `dotnet --list-sdks`
and install the selected version or an allowed stable patch. This checkout requires
the `10.0.4xx` SDK feature band; a runtime-only installation is insufficient.
Your target repository may select a different SDK through its own `global.json`.

**The `archguard` command is missing.** The CLI is currently installed from source.
Invoke `dotnet artifacts/cli/ArchitectureGuardian.Cli.dll` after following
[installation](installation.md). There is no global-tool installation step in this guide.

**A moved executable fails to start.** Copy the entire published directory, not just
its main DLL. Check that `dotnet` is available to the process launching the tool.
For desktop MCP clients, the process environment can differ from your terminal.

## Restore and workspace problems

**`restore-required`, unavailable references or compiler errors.** Restore the
target input explicitly, using the configuration/framework you intend to analyze.
Resolve failed restores and compilation problems before expecting full coverage.
The loader does not run restore and does not guarantee assets remain fresh after edits.

**`workspace-untrusted`.** CLI workspace commands require `--trusted`. MCP builds
require the operator to enable `Workspace:Trusted`; tool arguments cannot grant it.
Restore and MSBuild evaluation can execute repository build logic. Trust and
[isolation](workspace-loading.md#trust-and-isolation) are separate decisions.

**`framework-required` or `framework-unavailable`.** Specify `--framework` in CLI
or `targetFramework` in MCP. Every loaded project must declare that framework.
Use separate inputs/runs when frameworks differ across projects.

**A later MCP request needs another SDK.** MSBuild registration is process-wide.
Start another server process for a repository that requires a different SDK.

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

## MCP connection and path problems

**The client cannot start the server.** Publish the server first, point the client
at `ArchitectureGuardian.Mcp.dll` in the published directory and provide an existing
absolute `Workspace:Root`. Replace all template paths. Do not rely on shell variables,
`~` expansion or the client's current directory unless that client documents support.
Use `/` in Windows JSON paths or escape backslashes as `\\`.

**The client reports invalid protocol output.** Launch the published DLL with
`dotnet`, not `dotnet run`. The server reserves stdout for MCP; inspect stderr for
startup diagnostics. See [client configuration](mcp.md#configure-your-client).

**An explicit path is rejected.** Tool inputs must exist inside the configured root
and must not traverse descendant symbolic links/reparse points. Widen the
operator-selected root only when that is the intended workspace. This root check
does not sandbox trusted builds or their imports.

**The response exceeds 65536 bytes.** The tool returns an error instead of truncating
findings. Use supported metadata filters for `get_architecture`/`list_rules`, or a
smaller complete input/policy scope for `analyze_solution`. Do not interpret the
error as a clean scan. The [MCP reference](mcp.md) lists all limits.

**`analyze_changes` returns unsupported.** Graph comparison is not implemented yet.
The reserved interface has no passing result, counts or gate decision. Use a
full-solution scan for current-state analysis; it does not answer which changes
introduced a violation.

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
- Change comparison, baselines and report export are planned capabilities.

See [semantic accuracy](semantic-accuracy.md) for the rule-by-rule scope and
[contributing](../CONTRIBUTING.md#report-a-bug) for a useful issue report.
