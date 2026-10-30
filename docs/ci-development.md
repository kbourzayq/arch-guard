# CI gate development and validation

## Verify the integration locally

After the required Release solution build, run:

```sh
python3 eng/test-quality-gate.py
```

This executes real CLI processes against a temporary package-free Git repository,
checking clean comparison, new blocking findings, accepted debt, full severity
gating, unavailable base, invalid policy and stale-output refusal. It checks both
report formats and retained logs/summary on failures. Repository CI runs this check
on Linux, Windows and macOS; hosted template execution still requires your target
repository's SDKs, permissions, paths and branch-policy setup.

## Source-based pipeline templates

## Complete source-based pipeline examples

Copy and customize one complete example:

- [GitHub Actions](examples/ci/github-actions.yml): copy into `.github/workflows/architecture.yml`.
- [Azure Pipelines](examples/ci/azure-pipelines.yml): copy into `azure-pipelines.yml`.
  The artifact task targets Azure DevOps Services; on Azure DevOps Server use
  [Publish Build Artifacts](https://learn.microsoft.com/en-us/azure/devops/pipelines/tasks/reference/publish-build-artifacts-v1?view=azure-pipelines)
  instead of `PublishPipelineArtifact@1`.

Both examples intentionally build the CLI from a separate tool checkout. The
shared runner accepts a published DLL path through `--cli`; it does not accept an
installed `archguard` shim. Replace
`Application.slnx`, policy and configuration with your repository's paths/context.
Pin `GUARDIAN_REF`/`guardianRef` to a reviewed Architecture Guardian commit or tag
containing these examples before adopting a required gate; `main` demonstrates the
source-installation flow. Keep the tool checkout, published CLI, restored base
worktree and report directory outside the repository being scanned. Git comparison
includes non-ignored untracked files, so nesting another checkout can break capture.
Install Python 3, Git, the tool's exact SDK from `global.json`, and every SDK/reference
pack required by **both** target trees. Extend the SDK installation steps when
base and candidate select different versions. The examples install `10.0.402`.

## Checkout, comparison and restore

The examples use full history (`fetch-depth: 0` / `fetchDepth: 0`) and an explicit
base, with no implicit merge-base selection:

- GitHub compares the checked-out PR merge result against the event's exact target
  SHA, `github.event.pull_request.base.sha`. Use `pull_request`, with read-only
  repository access. Do not run untrusted PR builds with privileged secrets.
- Azure PR validation fetches `System.PullRequest.TargetBranch` and resolves it once
  to a commit. This compares the checked-out merge result against that fetched
  target snapshot, which can be newer than the PR event. The read-scoped checkout
  credential permits private-base fetching. For Azure Repos, YAML `pr` does not
  enable PR validation: configure this pipeline as a required **build validation**
  branch policy. GitHub-backed Azure pipelines use the YAML `pr` trigger.
- Azure manual runs use the explicitly configured `gateBaseRef`. Set it to the
  intended earlier commit/ref. Comparing a branch with its own current tip only
  checks for changes relative to that tip; it does not gate existing architecture debt.

GitHub checkout fetches the base history; Azure fetches its selected ref explicitly.
If you reduce fetch depth, ensure the selected base commit and its full tree are
locally available. A missing base is an operational failure, never a passing gate.
Input and policy must exist at the same repository-relative paths in both trees.
See [change analysis](changes.md) for capture, rename and unsupported-input semantics.

The preparation step creates a detached base worktree and runs an explicit locked
restore in **each** tree with the scan's configuration. Run each restore from its
own tree so its `global.json`, NuGet configuration and build inputs apply. Commit
package lock files and provide feed credentials through the CI provider when
needed. Both trees' packages must populate the same NuGet global cache used by the
CLI. If a repository overrides package/cache paths, configure a shared cache for
preparation and snapshots. For a selected framework, pass `--framework <tfm>` to
the runner and use matching restore properties for both trees.

Comparison preparation is cache-only and cannot download missing packages. Normal
scans require prior restore as well. A restore failure stops preparation and fails
the job; the artifact step still retains available preparation logs. There can be
no analysis report when the CLI has not reached an analysis result.
`--trusted` explicitly authorizes repository-controlled build logic in both trees;
run these examples only on agents permitted to execute those inputs.

## Choose the accepted-debt policy explicitly

The supplied examples use `--base <commit>`: only **new** findings at the candidate
policy's `gate.failOn` threshold block the gate. Existing findings remain visible.
They neither read nor create an accepted-debt file.

For a reviewed, committed debt inventory, replace `--base "$BASE_REF"` (GitHub) or
`--base "$base_commit"` (Azure) with `--baseline architecture.baseline.json` in the
runner invocation. Keep candidate restore; remove the base fetch/worktree/restore
and the Azure base-commit read, which are unnecessary for this mode. Missing or
invalid baseline files fail operationally. Never run `baseline update` in a gate:
review and commit updates separately using the [baseline workflow](baselines.md).
The two options are mutually exclusive.

To gate **all** findings without accepted debt, use `--all-findings` instead, keeping
candidate restore and removing base preparation as above. Require this full scan
for release approval alongside normal build/tests: a new-finding comparison can
pass while existing errors remain. Scans do not alter policy, baseline or source.

## Outcomes and retained artifacts

The shared [runner](../eng/run-quality-gate.py) invokes the standalone CLI twice,
once per encoding, using the same input, configuration and comparison. Keep inputs
stable until both scans finish. It requires an explicit `--base`, `--baseline` or
`--all-findings` choice and a fresh `--output-directory` on each invocation.

| Exit | CI result and log message |
| --- | --- |
| `0` | `PASSED`: both scans completed and passed the selected gate. |
| `1` | `POLICY FAILURE`: analysis completed with blocking findings. The step and job fail. |
| `2` | `OPERATIONAL FAILURE`: missing/unreliable workspace coverage, invalid arguments/policy/baseline, launch/report failure, or inconsistent scan exits. The step and job fail without approval. |

Both formats are attempted before the runner returns failure. Operational failures
take precedence over policy failures. The runner never uses `continue-on-error`,
accepts a nonzero code as success, or refreshes debt. A previously used output
directory is rejected to prevent stale reports from masking a failed invocation.

The `architecture-gate` artifact contains preparation logs and a `reports/` folder:

- `architecture.json` and `architecture.sarif`, when emitted by the CLI.
- Per-format stdout/stderr logs, including failures before reports exist.
- `summary.json` with the aggregate exit/outcome and each raw CLI exit/report path.

Both upload steps run with `always()`, so a failed scan does not skip artifact
publication. Agent loss, job cancellation/timeouts or storage/upload failures can
still prevent publication. An incomplete/failed analysis retains available findings
with a `notEvaluated` gate outcome; invalid policy or arguments can produce only
logs. Read the summary and [report status](reports.md), not just finding counts.

## Optional platform code scanning

The artifact gate is complete on its own. If GitHub code scanning is enabled and
your repository is eligible, you may add this step **after** artifact upload and
grant that job `security-events: write` in addition to `contents: read`:

```yaml
- name: Detect optional SARIF report
  id: sarif
  if: always()
  shell: bash
  run: |
    if test -f "$RUNNER_TEMP/architecture-gate/reports/architecture.sarif"; then
      echo 'available=true' >> "$GITHUB_OUTPUT"
    fi
- name: Optional SARIF code-scanning upload
  uses: github/codeql-action/upload-sarif@v4
  if: ${{ always() && steps.sarif.outputs.available == 'true' }}
  continue-on-error: true
  with:
    sarif_file: ${{ runner.temp }}/architecture-gate/reports/architecture.sarif
    checkout_path: repository
    category: architecture-guardian
```

SARIF locations are relative to the policy directory: adjust `checkout_path` if
your policy lives below the repository root. Availability and fork-token permissions
can prevent platform ingestion; this does not change the CLI gate or downloaded
artifacts. Azure users can download SARIF/JSON directly; no security extension is
required. See GitHub's [SARIF upload instructions](https://docs.github.com/en/code-security/code-scanning/integrating-with-code-scanning/uploading-a-sarif-file-to-github)
and Azure's [PR validation requirements](https://learn.microsoft.com/en-us/azure/devops/pipelines/repos/azure-repos-git?view=azure-devops#pr-triggers).
