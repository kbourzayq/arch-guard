# Full-context change analysis

CLI `scan --base <commit-or-ref>` and MCP `analyze_changes` compare **two complete
architecture contexts**, using the same loader, dependency graph, rules and stable
[finding fingerprints](baselines.md) as full scans. They do not filter changed
lines or restrict evaluation to edited projects. Changes to shared build inputs,
ownership, policy, rules or references can introduce findings in unchanged projects.

Install the [CLI](../README.md#install-the-cli) and run from your target repository
root. Replace `path/to/...` and `origin/main` with your input, policy and locally
available comparison base; prepare both trees as described below.

```sh
archguard scan path/to/Application.slnx --policy path/to/architecture.guardian.yaml --base origin/main --configuration Release --trusted
```

## What is compared

| Input | Semantics |
| --- | --- |
| Base | The exact locally resolved commit/ref (`<ref>^{commit}`), including that tree's solution, projects, source, build inputs and policy. Tags resolving to commits work. No implicit merge base. |
| Candidate | A captured copy of the current on-disk working tree, including all tracked paths and non-ignored untracked files selected by Git. |
| Staged and unstaged | Both participate through their final on-disk contents. If a file is staged and then edited again, the working-tree version wins. This is not an index-only scan. |
| Deleted paths | Working-tree deletions remove files from the candidate, even if the index retains an older version. |
| Ignored paths | Ignored untracked files, normally including `bin/` and `obj/`, are excluded. Tracked files remain included even if an ignore pattern matches them. |
| Build context | Both trees use the explicitly selected configuration/framework (Debug by default), with each tree's SDK selection and build inputs. |
| Dirty working tree | No clean-tree requirement, checkout, stash, reset or index update. Detected concurrent file/inventory changes during capture fail comparison; retry after edits stop. |

Refs are resolved once. There is no automatic fetch or network access to obtain
history. A missing ref, unavailable object or a base outside a shallow clone's
available history produces incomplete coverage. Fetch the intended base/history
explicitly, then retry. A shallow clone can compare any complete commit tree that
is present locally; it does not need unrelated history.

The explicit input and policy must exist at the **same repository-relative paths
in both trees**, and stay inside the Git root. Keep those entry paths stable when
comparing project removals or renames inside a solution. Each tree independently
loads its policy and resolves its complete project inventory. Update solution
membership, selectors and build references together when removing or renaming
projects; stale selectors and broken references remain coverage errors.

Comparison configuration/framework values accept letters, digits, spaces, `.`, `_`
and `-`. MSBuild separators, percent escapes and expressions are rejected before
preparation, including names declared in a solution: child builds can reinterpret
them as extra global properties. Use ordinary build-context names for comparisons.

## Snapshot preparation and trust

Git and the required .NET SDKs must be installed. Trust authorizes build logic in
**both** trees: CLI `--trusted` or operator-owned MCP `Workspace:Trusted=true`.
The MCP operator root must contain the Git root. Tool arguments cannot grant trust
or widen the operator root.

Analysis creates temporary base/candidate directories and a private Git index,
then prepares each snapshot using a bounded **cache-only locked restore**. A
temporary NuGet configuration clears package sources, an empty local feed overrides
project restore sources, and package auditing is disabled for this preparation.
[NuGet source mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping)
restricts every uncached package to that empty feed, including when SDK imports
reassign additional project sources. Snapshot SDKs must support source mapping
(SDK 6 or later); older selections fail before restore. Packages must already exist in the global cache;
installed SDK reference packs must cover both trees. Populate those requirements
through an explicit restore before comparison. Lock files, when present, must
agree with their tree's dependencies. Restore failures never yield an empty,
passing graph. Git attributes that omit files from archives do not omit snapshot
inputs.

Preparation does not fetch Git history or download packages. Ordinary full scans
continue to require explicit restore and do not restore automatically. The original
checkout's input files, index, refs and restore assets are not written by snapshot
preparation; temporary directories are removed on completion or cancellation.
Each Git/restore operation has a two-minute deadline and captures stdout/stderr;
cancellation terminates its process tree. Trusted repository tasks can still have
side effects: read-only MCP annotations and temporary copies are not a sandbox.
Git/SDK child stdin is redirected and closed so it cannot share the MCP protocol
pipe. This also avoids Git for Windows startup probes blocking on a pipe with a
pending server read. Deadline errors identify the command and whether it was
waiting for process exit or stdout/stderr EOF.

Symbolic links, reparse points, submodules and unresolved merges in snapshot inputs
are rejected. Ignored source or required prebuilt generators/binaries, inputs outside
the repository, and build logic requiring a live `.git` directory are not supplied
to isolated snapshots. Such contexts must fail loading; use complete supported
inputs and review coverage issues. Git filters run under the same trust as the base
checkout, with base-tree attributes rather than candidate attributes. No existing build outputs are deliberately reused as a substitute for
preparing the selected tree.

## Classification and gate

The base scan supplies the comparison inventory. Candidate diagnostics retain their
full evidence and severity and are classified as existing, new or resolved using
the [baseline matching contract](baselines.md). Source movement can retain a
fingerprint; changed project/module/symbol identities and policy constraints are
conservative new findings. Ambiguous matches remain new. Policy-only changes can
introduce or resolve findings without source changes. Severity-only changes do not
change fingerprints; the new-finding gate does not re-gate existing debt.

The candidate policy's severity threshold applies only to **new** findings.
CLI prints all candidate diagnostics, classification counts and the gate result:

| Exit | Meaning |
| --- | --- |
| `0` | Both scans completed and no new finding fails the candidate gate. |
| `1` | Both scans completed and a new finding fails the candidate gate. |
| `2` | Invalid input, unavailable ref/snapshot, incomplete or failed coverage, or cancellation. No passing gate. |

`--base` and `--baseline` are mutually exclusive. Git comparisons do not read or
update accepted-debt JSON files. Neither incomplete base coverage nor incomplete
candidate coverage can pass; missing findings are not called resolved until the
candidate completes. Existing debt is still visible and can pass comparison, so
retain full-scan validation and normal CI for [release approval](../CONTRIBUTING.md#release-validation).

MCP returns all candidate findings/counts and, when the base completed, a
`comparison` object with `baseCommit`, `existing` and `new` diagnostic-ID arrays,
and `resolved` base entries. Gate outcome is `not-evaluated` when either tree lacks
coverage. See the [MCP response contract](mcp.md#analyze_changes).
