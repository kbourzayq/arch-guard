# Example onboarding report

Illustrative report, not a run against this repository. Replace these values with
actual evidence and validation output; do not reuse them as observations.

- **Decision:** proposal reviewed; adoption pending. Active
  `architecture.guardian.yaml` and accepted-debt files remain untouched.
- **Evidence and intent:** `src/Shop.Domain/Shop.Domain.csproj` owns the domain
  types declared in `src/Shop.Domain/Order.cs`. The proposal assigns those projects
  to module Shop and layer Domain. `docs/architecture.md` describes inward layer
  dependencies. Clean Architecture is a suggestion consistent with that document,
  not a compliance result. No whole-project public contract has been approved.
- **Reviewable delta:** candidate `architecture.guardian.proposed.yaml` removes
  Application → Infrastructure permission from the approved policy. All six rules
  remain errors, `failOn: error`; no baseline change proposed.
- **Unresolved question:** who owns `src/Shared/Shared.csproj` in the additional
  integration solution? Ask the owner before extending scope. It was not part of
  the selected solution; no compliance claim covers that solution.
- **Discovery:** installed CLI 0.3.0 `init --help` has no `--discover`; discovery
  unavailable. Manual proposal from inspected paths and documented intent.
- **Exact validation:** CLI 0.3.0; input `Shop.slnx`; candidate policy above;
  Release/net10.0, restored explicitly after build authorization. `inspect` exit
  `0`; `scan --format json` exit `1`, status `completed`, gate `failed`.
  AG001–AG006 selected; threshold error. Debug and other frameworks not evaluated.
- **Finding and trade-off:** actual scan evidence identifies Application →
  Infrastructure as AG002. Treat as debt if the inward rule is approved; consider
  an application-owned abstraction and moving the implementation wiring to Host.
  Review API/lifetime impacts before authorizing source edits. Do not widen the
  candidate merely to pass. No source correction was requested or performed.
- **Adoption options:** full enforcement blocks on this error. A reviewed CLI
  baseline or explicit Git-base gate may retain it; neither has been selected or
  executed. A base without the policy at the same path is unavailable for comparison.
- **Outcome and limits:** candidate validation complete with failing gate; adopted
  validation not run because approval is pending. No operational issues in this
  context. Normal build/tests and hosted CI not run in this illustrative report.
  Runtime/reflection dependencies and namespace-only ownership remain unsupported.
