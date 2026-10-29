# MVP release sign-off: 0.3.0 candidate

This record tracks [#30](https://github.com/kbourzayq/arch-guard/issues/30).
**Release approval and publication remain pending.** Local candidate checks and
green main CI do not sign off a future tag. Keep #30 open until the exact release
workflow succeeds and all public packages are independently verified.

## Candidate scope and correctness

- Fix [#67](https://github.com/kbourzayq/arch-guard/issues/67): used function-pointer
  aliases traverse return and parameter types, including nested arrays, pointers,
  generics and function pointers. Repeated types are deduplicated per use; unused
  aliases remain imports. Located AG001/AG002/AG004/AG005/AG006 regressions cover
  legacy XML, YAML, independent permissions and the Roslyn 4.8 test host.
- The labeled semantic fixture checks parameter, return and nested aliases plus
  allowed contracts/unused imports in Debug/Release on net10.0/netstandard2.1.
- Installed package consumers check all five compiler rules for nested aliases in
  both policy formats, unused imports and allowed YAML contracts. No alias waiver
  is proposed.
- Package smoke tests inspect ownership, exercise all six solution rules, explain
  a current diagnostic and correct fixture source/reference from the client.
  Independent installed CLI and MCP scans must then complete and pass the unchanged
  policy. Missing-input execution must return operational exit 2.
- [#69](https://github.com/kbourzayq/arch-guard/issues/69) remains a known message
  limitation: oversized analysis/explanation errors can suggest unsupported filters.
  Use a smaller complete input/policy scope. The 65536-byte cap still fails
  explicitly; results are not truncated or represented as clean.
- [#68](https://github.com/kbourzayq/arch-guard/issues/68) is implemented by
  [#84](https://github.com/kbourzayq/arch-guard/pull/84). Evaluated `AdditionalFiles`
  now contains one entry each for shipped/unshipped release tracking. Diagnostic
  formatting verifies zero changed files without duplicate-input warnings.
- [#74](https://github.com/kbourzayq/arch-guard/issues/74) is also implemented by
  #84. The existing repository validator builds with compiler enforcement and scans
  all six rules; the independent quality-gate scenarios cover policy failure and
  incomplete execution. Retain both scripts and the three-OS CI jobs.
- [#73](https://github.com/kbourzayq/arch-guard/issues/73) remains a lock-policy
  follow-up. This release retains committed locks and locked restore.

The XML transition is unchanged: 0.3.0 is the first YAML minor and retains legacy
XML; removal is planned for the following minor. See the
[migration guide](compiler-analyzer.md#yaml-ownership-and-migration).
Analyzer release tracking records the existing AG000/AG001/AG002/AG004/AG005/AG006
rules as shipped in 0.2.0. The alias fix changes detection, without introducing a
new rule ID or severity default.

## Version and changelog preparation

Pinned Commitizen 4.19.3 previews **0.3.0 / MINOR** from main
`2adae5e1173830f6f4a723977ad6fdcad895f881`. Its default ancestry-based changelog
repeats the distribution feature: immutable `v0.2.0` points to PR head `30e2249`,
while main contains squash `b79f714`. Keep the old tag and old package bytes.

The candidate files were synchronized with
`cz bump --check-consistency --version-files-only --yes` after independent review
of the implementation commit. The new release section was generated with the
explicit shipped main boundary:

```sh
cz changelog --start-rev b79f714df07b1d8a7bf8c6f260111d580ed75533 --unreleased-version v0.3.0 --dry-run
```

The generated output supplies the new section and retains the existing generated
0.2.0 section verbatim. Notes include the alias fix, shared YAML, performance/local
telemetry and the agent workflow; synchronized examples use 0.3.0. XML migration
timing and analyzer release tracking were reviewed above. This files-only
preparation created no release tag and does not authorize publication.

Do **not** rerun `cz bump` to finalize this candidate: the source version is already
0.3.0, so another bump would select the following release. After explicit maintainer
approval, merge the reviewed candidate PR, confirm normal CI and the 0.3.0 files on
the exact approved merged commit, then create the immutable tag at that commit.
Replace `APPROVED_COMMIT_SHA` with that reviewed full SHA:

```sh
git tag v0.3.0 APPROVED_COMMIT_SHA
git push origin refs/tags/v0.3.0
```

The existing release workflow validates that exact tagged commit and publishes its
original tested artifacts. Require the tagged run to succeed, confirm all three
0.3.0 public feed versions, then record final sign-off. Never move the tag or
republish an existing version to repair a failure.

## Evidence required for final approval

Local candidate validation on 2026-10-29 passed locked restore, zero-warning
Release build, formatting, repository compiler/self-scan (zero findings), all
**619** solution tests (111 compiler, 148 architecture/CLI, 360 unit/workspace),
and the expanded local-package pack/verify/smoke checks. An initial suite run
overlapped package rebuilds and had one
CLI process assertion failure; the isolated reference group and subsequent complete suite
both passed after rebuilds finished. Run package rebuilds and process tests
sequentially in a shared checkout.

Report validation passed all 20 serializer samples and four real CLI exports;
independent gates passed clean, blocking, accepted-debt, full, missing-base,
invalid-policy and stale-output scenarios. Independent review found no remaining
correctness blockers in the analyzer or candidate-package changes.


| Gate | Evidence to retain |
| --- | --- |
| Local candidate | Locked restore, zero-warning Release build, formatting, full solution tests, report schemas, independent gates, repository self-validation and package pack/verify/smoke results |
| Agent correction and comparisons | `CorrectionWorkflowTests`, baseline/new-finding tests and `ChangeAnalysisTests` compare CLI `--base`/MCP `analyze_changes` with full graph scans; retain their exact candidate CI results |
| Independent review | Reviewer findings and resolutions for the complete PR diff, including new scripts and version/generated files |
| Exact tag | Approved immutable `v0.3.0` commit SHA and its **Release NuGet packages** workflow URL; all OS test/package matrices and required aggregate gates succeed |
| Public availability | Confirm that the approved version is available in the public NuGet feed for CLI, MCP and Analyzers, and retain the three feed URLs; no additional package-download or post-publication smoke stage is required |
| Maintainer sign-off | Record the exact tag/workflow/feed evidence and accepted remaining limits before closing #30 |

On 2026-10-29, the public NuGet indexes list only **0.2.0** for all three packages:
[CLI](https://www.nuget.org/packages/ArchitectureGuardian.Cli/0.2.0),
[MCP](https://www.nuget.org/packages/ArchitectureGuardian.Mcp/0.2.0), and
[Analyzers](https://www.nuget.org/packages/ArchitectureGuardian.Analyzers/0.2.0).
Baseline main [CI 40830760526](https://github.com/kbourzayq/arch-guard/actions/runs/40830760526)
passed at `2adae5e`; this is not candidate/tagged CI evidence.

Tagging, merging and trusted publication require the maintainer's separate release
decision. Do not publish local development packages labeled 0.2.0, rebuild
substitutes for tested artifacts, or close audit issues from this record alone.
