# Independent CI quality gates

Install the released CLI on your runner, restore the build context, and let its
process exit code decide the architecture gate. No Architecture Guardian source
build, MCP server or code-scanning service is required.

## Gate with the installed CLI

First [install the CLI](install.md#install-the-cli) from a directory selecting
SDK 10. Then run from your repository root after authorizing its build logic. Replace
`MyApp.slnx` and the policy path with your own:

```sh
dotnet restore MyApp.slnx -p:Configuration=Release
archguard scan MyApp.slnx --policy architecture.guardian.yaml --configuration Release --trusted --format sarif --output architecture.sarif
```

Provision .NET 10 for the tool plus all SDKs/reference packs required by your input.
The target repository's `global.json` applies to restore and workspace loading.
Use the same configuration/framework in preparation and scan. If you maintain
NuGet lock files, add `--locked-mode` to restore. Do not add it without first
creating and committing the required lock files.

## Complete pipeline examples

Copy an installed-package template and adapt its input/policy/context:

- [GitHub Actions](examples/ci/github-actions-installed.yml): save as `.github/workflows/architecture.yml`.
- [Azure Pipelines](examples/ci/azure-pipelines-installed.yml): save as `azure-pipelines.yml`.

Both install the CLI from and into the runner's temporary directory so a target
`global.json` cannot select an older SDK during tool installation. Restore and
scan still run in the target checkout. Both scan all selected findings and preserve
SARIF after a failed gate. Add any target SDKs beyond .NET 10.
They use Linux runners; choose an appropriate executable path when adapting to Windows.
The templates authorize repository MSBuild logic with `--trusted`; use runners
permitted to execute the inputs. Keep secrets and privileged access out of untrusted PR builds.

For Azure Repos, enable a required build-validation branch policy. YAML `pr` alone
does not enable Azure Repos PR validation. The artifact task targets Azure DevOps
Services; Azure DevOps Server uses its Publish Build Artifacts task instead.

## Choose the accepted-debt policy explicitly

| Gate mode | Scan option | What blocks the gate |
| --- | --- | --- |
| Full architecture gate | No baseline/base option | Every finding at or above the policy threshold |
| Reviewed accepted debt | `--baseline architecture.baseline.json` | New findings at or above the threshold |
| Explicit Git comparison | `--base <commit>` | Findings newly introduced relative to that complete base context |

The templates use the full gate. For baseline mode, create and review the baseline
outside CI, commit it, then add the option to the scan. Never run `baseline update`
automatically to make a pipeline pass. Missing/invalid baselines fail operationally.
Use one independently reviewed baseline per gated build context.

For Git comparison, prepare the base as described below before adding `--base`.
`--base` and `--baseline` are mutually exclusive. A passing new-finding gate can
retain existing errors; keep a full scan for release approval and alongside normal
build/tests. See [baselines](baselines.md) and [change analysis](changes.md).

## Checkout, comparison and restore

A Git comparison needs an explicit base commit and both trees' complete build
requirements. It does not fetch history or download missing packages for you.

1. Fetch sufficient history for the intended base. On GitHub Actions,
   `fetch-depth: 0` is one simple choice; resolve an explicit target SHA, such as
   the PR event's `github.event.pull_request.base.sha`.
2. Prepare both base and candidate in the same intended configuration/framework.
   Run restore from each tree so its SDK, NuGet configuration and build inputs apply.
3. Ensure both restores populate the package cache used by the CLI snapshots.
   Install both trees' SDKs/reference packs and supply feed credentials during preparation.
4. Keep preparation worktrees, installed tools and report outputs outside the scanned
   repository. Comparison includes non-ignored untracked files.
5. Invoke `scan --base <resolved-commit>` from the candidate tree. Require completed
   coverage for both trees and a passed new-finding gate.

Input and policy must exist at the same repository-relative paths in both trees.
Cache-only snapshot restore cannot repair a missing SDK, unavailable base or empty
package cache. Those conditions return exit `2`, not an approved comparison.
For full/baseline scans, only the candidate context needs preparation.

## Outcomes and retained artifacts

| Exit | Pipeline meaning |
| --- | --- |
| `0` | Analysis completed and the selected gate passed |
| `1` | Analysis completed with blocking architecture findings |
| `2` | Input, coverage, cancellation, launch or reporting failure; compliance remains unresolved |

Let either nonzero exit fail the step/job. Avoid `continue-on-error`, ignored exits
or scripts that end with a successful command after a failed scan. Configure artifact
upload with an always-run condition; logs remain useful when failure occurs before
a report can be emitted. Ensure an output file's parent directory exists and use
fresh output paths to avoid mistaking an old report for the current result.

Use `--format json` instead for JSON v1. If exporting both formats through separate
scans, keep the inputs unchanged and check each scan's exit code. Neither encoding
changes gate semantics. See [report contracts](reports.md).

## Optional platform code scanning

SARIF can also be uploaded to a compatible code-scanning service. That ingestion is
separate from the process-exit gate and depends on provider features, permissions
and fork-token policy. Downloadable SARIF/JSON artifacts work without it.
SARIF locations are relative to the policy directory; configure the platform's
checkout/source root accordingly. Keep [coverage and gate outcomes](concepts/diagnostics.md)
visible even when the platform displays individual findings.
