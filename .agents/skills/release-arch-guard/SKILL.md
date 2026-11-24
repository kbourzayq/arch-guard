---
name: release-arch-guard
description: "Prepare or publish a stable Architecture Guardian release through Commitizen, a release PR, GitHub Actions, NuGet, and GitHub Pages. Use for maintainer requests to release Arch Guard, prepare its release PR, or resume an in-progress release."
---

# Release Architecture Guardian

An explicit request to release a version authorizes the release PR, squash merge,
tag push, and existing publishing workflows within the requested scope. For
preparation only, default to a validated open release PR. Honor any earlier
handoff and any instruction to hold the tag. Creating, inspecting, or validating
this skill does not authorize a release or a public announcement.

Read [AGENTS.md](../../../AGENTS.md), the [release
guide](../../../docs/release-process.md), and the [release validation
requirements](../../../CONTRIBUTING.md#release-validation). Use the SDK and
Commitizen versions pinned by the repository.

## Establish release state

- Check the working tree, the remote for `kbourzayq/arch-guard`, and the latest
  remote `main`. Isolate unrelated changes and preserve them.
- Compare the source version in [`.cz.toml`](../../../.cz.toml) and
  [`Directory.Build.props`](../../../Directory.Build.props) with the latest
  stable GitHub release, tags, and all three NuGet package versions. Use the
  requested stable version, or identify the pending version if asked to resume.
  Use Commitizen to propose the next stable version for a new release whose
  version is unspecified.
- Before bumping, look for a matching release PR across all states, a merged
  release commit, and the requested local and remote tag. Resume at the first
  unfinished authorized step when the version, changelog, and commit agree.
  Stop if the release identity is ambiguous, a tag points at a different commit,
  or existing package versions cannot be tied to the verified release commit.
  If the release is already published, verify its outputs and report completion.
  If starting fresh, create a dedicated branch from current `main`, such as
  `chore/release-<version>`.

## Prepare and validate the release PR

1. Run `cz bump --check-consistency --dry-run --yes "$version"` with the
   selected version. To discover the next version, first run it without a
   version argument. Record the exact rendered `bump_message` from the preview
   for the release commit and squash subject.
2. Run `cz bump --check-consistency --version-files-only --yes "$version"`.
   Confirm that it generated a nonempty `## v<version> (date)` changelog section
   covering changes since the prior release and preserved historical entries.
   Keep changelog generation in Commitizen. Skip the bump if preparation already
   produced the requested version and heading.
3. Review remaining references to the previous version. Update active install,
   upgrade, and CI examples and any affected package metadata or compatibility
   floors. Regenerate affected .NET lock files through restore. Keep historical
   references, third-party version pins, and shipped pack manifests intact.
   If a pack is shipping for the first time and requires the new engine,
   set its minimum version accurately and update its expected manifest hash.
4. Run the required locked restore, Release build, format verification, and
   solution tests, plus the applicable release checks in the contribution guide.
   Validate any affected benchmark lock with locked restore and check the
   documentation build. Record actual results and resolve failures.
5. Use [commit-push-pr](../commit-push-pr/SKILL.md) to commit the intended changes
   with the exact Commitizen subject recorded in step 1. If the version was
   already bumped, recover the expected subject from the base and target versions
   and the configured template. Before pushing, run `eng/packages.py` with
   `pack`, `verify --tag "$tag"`, and `smoke` against the same fresh package
   directory, where `tag="v$version"`. This keeps package metadata tied to
   the release branch commit.
6. Use `git push --no-follow-tags` for the release branch and create or reuse
   its PR against `main`. Wait for CI and documentation checks on the exact PR
   head before merging or handing off a validated PR. For a preparation-only
   request, stop here.

## Merge, tag, and publish

1. Squash-merge with the same Commitizen subject and require the expected PR
   head SHA. Fetch `main`, record the returned squash SHA, and verify the
   commit is on remote `main` and contains the reviewed release tree, version,
   and changelog. Check that CI on `main` passed for that exact SHA. A failing
   run, changed base or head that invalidates the tested tree, or an unresolved
   branch protection gate blocks tagging.
2. If the user asked to hold the tag, stop after reporting the verified merge.
   Otherwise, recheck the remote tag and package versions. If the remote tag's
   commit matches the verified squash commit, follow the existing tagged run.
   When the remote tag is absent, create it with `git tag "$tag" "$merge_commit"`
   if it is also missing locally. Confirm its peeled commit, then use
   `git push --no-follow-tags "$remote" "refs/tags/$tag:refs/tags/$tag"`
   to push that tag alone. Recheck the remote after an ambiguous push. Never
   move, delete, or force-push a release tag.
3. Follow the [tagged release workflow](../../../.github/workflows/release.yml)
   for that tag and SHA. It runs the full CI matrix and uses NuGet trusted
   publishing to upload the tested artifacts. It then waits for package listing
   and creates the GitHub release from the tagged changelog. Continue watching
   while indexing is pending. Do not upload local packages or create an early
   GitHub release to bypass those steps.
4. Confirm that NuGet publicly lists the same version for
   `ArchitectureGuardian.Cli`, `ArchitectureGuardian.Mcp`, and
   `ArchitectureGuardian.Analyzers`. Check each public package's project website
   against `PackageProjectUrl` and its repository commit against the tag. Confirm
   GitHub marks the forward stable release as Latest and its notes link to the
   tagged changelog. Wait for the Pages deployment and verify the live
   `release.json` reports the same tag, version, and commit.

## Recover and report

If a run fails, identify the failed job and check actual state before retrying.
During or after the NuGet upload step, check which versions were accepted and
which are public. A successful upload with delayed indexing calls for waiting.
For a partial upload, identify the exact missing packages. Do not retry the
whole upload after any version was accepted. Report the blocker if there is no
documented recovery that uses the original tested artifacts and preserves
accepted versions. Retry downstream GitHub release or Pages steps independently
when the original artifacts are public, using the
[release guide](../../../docs/release-process.md) for same-tag recovery. Respect
any environment approval configured in GitHub.

Report the release version, exact commit and subject, PR, workflow, public
packages, and Pages URLs as applicable. State which checks ran locally and
which passed on GitHub. If a requested stage is blocked, report the last
verified stage and the concrete blocker.
