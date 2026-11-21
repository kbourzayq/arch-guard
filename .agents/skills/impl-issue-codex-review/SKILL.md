---
name: impl-issue-codex-review
description: "Implement an issue, have Codex review the finished change once (default model sol, gpt-6.2-sol), fix every valid finding with a test that proves it, then stop with a verified local change. Use when asked to implement an issue with a Codex review; not for review alone."
---

# Implement an Issue with a Codex Review

Accept an issue number or URL, plus an optional `--model <name>`. The default model is `sol`, which maps to `gpt-6.2-sol`; any other value is passed to Codex unchanged.

Work autonomously. Make routine implementation and review-triage decisions without asking the user to approve them. Ask only when missing information would change behavior or acceptance criteria, as [implement-issue](../implement-issue/SKILL.md) describes.

## Implement

Complete the issue with [implement-issue](../implement-issue/SKILL.md). That covers its same-checkout semantic branch, implementation, tests, documentation, drift reconciliation and the full CI checks required by `AGENTS.md`. Only start the review once that work is verified.

## Review once with Codex

- Locate the Codex companion script at `~/.claude/plugins/cache/openai-codex/codex/<version>/scripts/codex-companion.mjs`, choosing the highest installed version. If it is missing or Codex is not set up, report the review as blocked and point to `/codex:setup`. Never present a self-review in place of the Codex review.
- From the repository root, run exactly one review of the working tree in the background. This includes untracked new files. Write the output to the session scratchpad:

  ```sh
  node <script> review --scope working-tree --model <model> --wait > <scratchpad>/codex-review.txt 2>&1
  ```

- Wait for the background completion notification instead of polling. Then read the `# Codex Review` section.

## Triage, fix and prove

Evaluate every finding against the issue, `AGENTS.md` and the repository's contracts:

- Fix each valid finding in the appropriate source. Do not weaken rules, checks or baselines to make a finding disappear.
- Prove each fix with a focused test or fixture case that fails without the fix. If a fix cannot be reproduced in a test, say so and explain why.
- Update documentation, support text and derived artifacts affected by the fixes.
- Record each finding rejected as invalid, with a one-line reason.
- Rerun the full CI checks after the fixes.

Do not run a second Codex review. The tests that prove the fixes, plus a passing CI run, are the evidence that the findings are resolved.

## Finish

Do not commit, push or open a pull request. Report:

- the branch and issue reference, and the implemented behavior;
- a findings table listing each Codex finding as **fixed**, naming the test or fixture that proves it, or **rejected**, with the reason;
- validation results, stating that they are local runs and not a GitHub Actions run;
- remaining limitations and any blocked checks.

Then offer [commit-push-pr](../commit-push-pr/SKILL.md) as the next step.
