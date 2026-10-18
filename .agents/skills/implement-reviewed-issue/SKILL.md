---
name: implement-reviewed-issue
description: "Implement an issue, run a sub-agent review after development, resolve findings, then commit, push and create or reuse its pull request. Use when the user requests this complete reviewed issue-to-PR workflow."
---

# Implement, Review and Publish an Issue

Accept an issue number or URL. Complete the issue using [implement-issue](../implement-issue/SKILL.md), including its same-checkout semantic branch, documentation, drift reconciliation and verification requirements.

After implementation and relevant checks are complete, delegate an independent code review to a sub-agent. Supply the issue acceptance criteria, repository and branch/base, the full intended diff (including new files), applicable instructions and validation results. Ask for actionable correctness, regression, coverage and documentation findings with file/line references. The reviewer should inspect the change without editing it or publishing anything. If sub-agents are unavailable, report that the requested independent review is blocked; do not present a self-review as its replacement or publish before that requirement is resolved.

Evaluate the review findings against the issue and repository contracts. Fix valid findings, run checks covering the corrections, and request a focused follow-up review when substantive changes warrant it. Resolve any material disagreement explicitly. Do not publish with an unresolved correctness blocker.

Once development, verification and review are finished, use [commit-push-pr](../commit-push-pr/SKILL.md) to commit the intended changes, push the issue branch, and create or reuse its PR. Invoking this complete workflow authorizes these publishing steps; respect any narrower user instruction. Follow that skill's lookup across all PR states, destination checks and attachment behavior. Do not merge, deploy or post separate issue comments without separate authorization.

Report the issue, branch, implemented behavior, checks, review outcome, commit and PR link, plus any remaining limitations. A blocked review or publishing step must be stated accurately while preserving completed local work.
