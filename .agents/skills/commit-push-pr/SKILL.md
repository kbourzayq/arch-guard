---
name: commit-push-pr
description: "Commit repository changes using Conventional Commits, push the branch, and reuse its existing GitHub pull request or create one only when none exists. Use when asked to publish repository work or run this commit-push-PR workflow; respect requests limited to only some of these actions."
---

# Commit, Push, and Open a PR

Publish the intended repository change as a Conventional Commit on a semantic branch and a pull request against the correct repository and base.

An explicit `$commit-push-pr` invocation requests all three actions unless the user limits the scope. When selected implicitly, perform only the actions the user requested: a commit-only request does not authorize pushing or opening a PR. Creating or editing this skill does not invoke its workflow. Once execution is authorized, proceed through the requested actions without asking again for routine confirmation.

## Establish the change and destination

- Read applicable `AGENTS.md`, contribution guidance, commit conventions, and PR templates. Inspect the current branch, staged and unstaged diffs, untracked files, remotes, upstream, and existing commits that would be published.
- When understanding or locating code in a repository with `.codegraph/`, use CodeGraph exploration before text searches or source reads. Ordinary Git status, history, and diff inspection do not require a code query. Do not create an index.
- Derive the intended scope from the user's request and the work completed in the conversation. Preserve unrelated edits and staged changes. If scope is materially ambiguous, ask which changes belong; do not assume every modified or untracked file is part of the task.
- Resolve the target repository and push remote from explicit user instructions, the branch's upstream, and repository conventions. Account for fork workflows: the writable head repository and the PR's target repository may differ. Do not assume `origin` is the PR target.
- Choose the user-specified base first, then the documented contribution base, otherwise the target repository's remote default branch. Inspect the full branch diff and commits against that base, including older unpushed commits, so the PR contains only intended work.
- Reuse the appropriate working branch. If currently on the default/protected branch or a detached HEAD, create a branch that preserves the intended commits and working changes. Use `<type>/<issue-number>-<short-slug>` when an issue is known, otherwise `<type>/<short-slug>`. Start with the semantic type, without `codex/`, and validate the name with `git check-ref-format --branch`. Do not reset or rewrite existing work to obtain a clean branch.

## Prepare and commit

Run the repository checks required for the changed area, including relevant documentation and generated-artifact or drift checks. Reuse passing results when they still cover the same contents. Fix failures caused by the change before publishing; describe pre-existing failures or unavailable checks accurately and respect repository release or PR gates.

Stage only intended paths or hunks. Preserve unrelated entries in the index; avoid blanket staging commands when the working tree contains other work. Inspect the exact staged diff, including newly added files. Exclude credentials, machine-local settings, logs, and tool indexes such as `.codegraph/` unless they are deliberately maintained repository content. If a staged file mixes unrelated work, isolate the intended hunks or ask when the boundary cannot be determined.

Use this Conventional Commit form:

```text
type(optional-scope)!: concise imperative summary

Optional explanation of the reason and resulting behavior.

Optional footers, such as Refs #123.
BREAKING CHANGE: describe an actual breaking contract and migration, when applicable.
```

The scope, `!`, body, and footers are optional. Include `!` or a `BREAKING CHANGE:` footer only for an actual breaking change; when a footer is used, separate the footer block from the preceding text with a blank line.

Choose the type from the actual diff and the repository's accepted types. Common types are `feat`, `fix`, `perf`, `refactor`, `docs`, `test`, `build`, `ci`, `style`, and `chore`. Supporting tests and docs retain the main change's type. Use a scope only when it identifies a meaningful component. For example:

```text
feat(dependencies): add repository dependency graph
fix(cli): handle empty workspace paths
docs: explain dependency graph output
```

Create a focused commit, or separate commits when distinct requested changes warrant them. Use a message file for multiline messages. Do not amend existing commits, change global Git identity, or bypass hooks unless the user has authorized it. If identity or signing configuration is missing, report the specific blocker rather than inventing credentials or author details.

After committing, inspect the resulting commit and working tree. Account for hook changes before proceeding. If the intended work is already committed, reuse those commits instead of creating an empty or duplicate commit.

## Push the branch

- Before publishing, confirm the branch, destination remote, and outgoing commits still match the intended scope. Check authentication using the available GitHub connector or CLI without exposing tokens.
- Push normally to the chosen writable remote, setting the upstream when needed. Do not force-push, delete branches, or rewrite shared history as part of this workflow.
- Verify that the push succeeded and the expected commit is present on the remote branch. If rejected, inspect the cause and retry only after a concrete correction. Do not retry authentication failures blindly or force an update to overcome divergence.
- If the branch is already pushed at the desired commit, continue to the PR step without another commit or unnecessary push.

## Reuse an existing PR or create one

Before attempting PR creation, use an available GitHub connector or `gh pr list --repo OWNER/REPO --head BRANCH --state all` to check the target repository for related PRs across all states, including drafts, closed PRs, and merged PRs. Match the actual head repository/owner and branch, including fork identity; titles and issue references alone do not establish a match. Do not filter by the proposed base, since a related PR may target another base.

- If a matching open or draft PR exists, skip PR creation. Pushing to its head branch updates its commits automatically. Reuse its URL and update its description when needed to reflect the final change; do not silently retarget its base or change its draft/review status.
- If a matching closed or merged PR exists, skip PR creation and report its URL and state. Do not automatically reopen it or create a replacement PR.
- If several PRs match, report the matches and skip PR creation rather than choosing arbitrarily or adding another PR.
- Only create a PR when a successful, complete lookup confirms that no related PR exists. A failed, incomplete, or ambiguous lookup is not proof of absence; report the lookup blocker and skip creation.

Prepare the title and description from the full PR diff, not merely the latest commit. Follow the repository template. Without a template, explain the concrete problem and resulting behavior, then provide relevant validation and limitations. Use a Conventional Commit style title unless the repository specifies otherwise. Link a known issue; use closing keywords only when the PR fully resolves it and the user's request or repository workflow calls for closure. Do not invent issue numbers, test results, or reviewers.

Immediately before creation, repeat the branch lookup to catch a PR created during the workflow; if one exists, follow the reuse rules above and skip creation. For CLI creation, pass the target repository, base, and head explicitly; qualify the head with its owner when needed for a fork. Write multiline descriptions to a file and pass `--body-file` rather than embedding shell-sensitive text in a command. Create a normal PR for completed work unless the user requests a draft or repository policy requires one.

If PR creation times out or returns an ambiguous result, repeat the same branch lookup before retrying. If it finds a related PR, reuse it and do not call PR creation again. After successful creation, attach its URL to the current chat using `mcp__codex_app__attach_artifact` with `artifact_type: pull_request` when that tool is available. Also attach an existing PR when continuing work on it. An attachment failure does not justify creating another PR.

Do not merge, enable auto-merge, deploy, or post separate issue comments as part of this workflow unless separately requested. If committing succeeds but pushing or PR creation fails, preserve the successful work and report exactly where execution stopped.

Finish with the commit SHA and subject, branch, PR link and state, validation results, and any remaining blocker. State whether the PR was reused, newly created, or creation was skipped, and make clear which requested actions completed.
