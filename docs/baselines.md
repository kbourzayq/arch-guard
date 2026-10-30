# Diagnostic baselines and new-violation gates

A baseline is an explicitly reviewed inventory of accepted architecture debt.
A baseline-aware scan reports every finding and fails only for **new** findings
at or above the policy's `gate.failOn` severity. Incomplete, failed or cancelled
analysis still exits `2`, regardless of the baseline or available findings.

## Create or update accepted debt

Install the [CLI](install.md#install-the-cli). From your target repository root,
replace `path/to/...` with the real input, policy and baseline paths. Authorize
build execution, restore the same configuration, then run an explicit update:

```sh
dotnet restore path/to/Application.slnx -p:Configuration=Release
```

```sh
archguard baseline update path/to/Application.slnx --policy path/to/architecture.guardian.yaml --baseline path/to/architecture.baseline.json --trusted --configuration Release
```

The command replaces the complete inventory with the current findings, including
warnings and errors. It returns `0` when complete analysis was saved, even when
those findings would fail a regular severity gate. Review the JSON diff and commit
it alongside the intended policy/source change. There is no automatic baseline
creation, discovery, merging, expiration or scan-time acceptance.

Updating requires completed coverage. Invalid inputs, loading errors and
cancellation return `2` and preserve any prior file. Writes use a temporary sibling
and atomic replacement; the parent directory must exist. Repeating an update for
the same semantic findings produces identical bytes, with no timestamps or local
absolute paths added by the writer.

## Scan against a baseline

```sh
archguard scan path/to/Application.slnx --policy path/to/architecture.guardian.yaml --baseline path/to/architecture.baseline.json --trusted --configuration Release
```

All current diagnostics retain their full evidence, severity and snapshot IDs.
After complete analysis, output classifies current diagnostic IDs as `Existing`
or `New` and prints absent baseline fingerprints as `Resolved`, with counts.
Only new findings participate in the inclusive severity threshold. Existing errors
remain visible but can pass. Resolved entries stay in the file until an explicit
update. Missing, malformed, inconsistent or unsupported baseline files fail with
exit `2`; a missing file never means an empty accepted inventory.

Without `--baseline` or `--base`, CLI scans gate **all** findings. The YAML policy
schema is unchanged. MCP `analyze_solution` applies the full severity gate;
`analyze_changes` gates new findings against an explicit Git base. Neither reads
an accepted-debt JSON file. Compiler diagnostics use `.editorconfig` and the host
build result rather than a solution severity gate or baseline. Baseline persistence
is exposed by CLI and the reusable Analysis store/Core matching contracts.

## Version-one identity and matching

The JSON envelope contains integer `version: 1` and an `entries` array. Each entry
contains `fingerprint`, `ruleId`, nullable `module`/`symbol`, ordered
`dependencyPath` endpoints (`kind`, `identity`), sorted `constraints`
(`scope`, `setting`, sorted `values`), and nullable `snapshotId`. Every field is
required, including nullable fields. Empty inventories are valid. Unknown or
duplicate JSON fields, null collection entries and mismatched digests are invalid.
Changing formats requires a new schema/fingerprint version; unsupported versions
require explicit regeneration with a compatible tool.

The fingerprint is lowercase SHA-256 over an invariant length-prefixed encoding
of `ag-baseline-v1`, rule ID, module, symbol, ordered semantic path, sorted violated
constraints and optional fallback snapshot ID. Null and empty differ; comparisons
are ordinal and case-sensitive. Severity, source coordinates, messages,
remediation, evidence text and public-contract suggestions do not enter a semantic
fingerprint. The separate diagnostic snapshot ID retains its original meaning.

| Change or match | Behavior |
| --- | --- |
| Lines/columns shift, a source file moves or is renamed within the same project | Existing when the rule, target and semantic context stay the same |
| More source evidence for the same grouped project/symbol violation | Existing; rules still report all current evidence |
| Symbol/namespace, logical module, project path or violated constraint changes | Old entry resolved and current finding new after complete analysis; no inferred rename mapping |
| Severity changes | Identity unchanged; new findings use current severity |
| Representative module-cycle path changes | New identity, even if some cycle members remain |
| No structured evaluator context (for example, a custom rule) | Exact snapshot ID fallback; movement/wording changes conservatively count as new |
| Exactly one current and one baseline entry share a fingerprint | Existing |
| Several current or baseline entries share a fingerprint | All current candidates new; none silently accepted; still-present ambiguous entries are not called resolved |
| Incomplete analysis | No gate decision or resolution claims, no update; available findings remain visible |

There is no fuzzy matching by filename, line proximity, symbol short name or
similar text. Removing ambiguity or renaming identities requires explicit review
and baseline regeneration. A baseline records only the supplied analysis scope;
absence means resolved **within that scope**, not proof about other configurations,
frameworks or unselected rules. Use separate baseline files for independently gated
inputs/build contexts, and review rule selection changes with their baseline diffs.

Core owns immutable `BaselineEntry`, `BaselineSnapshot`, `BaselineComparison` and
`GatePolicy.PassesNewFindings`; Analysis owns `JsonBaselineStore` file/JSON handling.
No serializer or filesystem dependency is added to Core. Fixtures in
`BaselineTests`, `JsonBaselineStoreTests` and `CliWorkspaceTests` cover movement,
renames, ambiguity, severity, persistence and operational failure behavior.

## Git comparison

`scan --base <commit-or-ref>` builds its comparison inventory from a complete scan
of that Git tree, using the same stable fingerprints described here. It uses each
tree's policy and gates new candidate findings. It does not read or write a JSON
baseline, and cannot combine with `--baseline`. See [change analysis](changes.md).
