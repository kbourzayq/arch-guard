# Machine-readable reports

CLI `scan` supports `--format json` and `--format sarif`, including scans with an
explicit `--baseline` or Git `--base`. The default is `--format text`. Arguments
are case-sensitive; `inspect`, `load`, `explain` and `baseline update` do not export
reports. See [CLI usage](cli.md#export-json-or-sarif-reports).

Without `--output`, stdout contains exactly one report document and a final LF.
With `--output <path>`, stdout is empty and the destination receives UTF-8 without
a BOM. The parent directory must exist. A sibling temporary file is written before
atomic replacement; failed or cancelled writes before replacement preserve the
previous destination. Scans never update baselines implicitly.

Analysis completion and policy compliance stay separate. Exit `0` means completed
analysis and a passed gate; `1` means completed analysis and a failed gate; `2`
means invalid input, unreliable coverage, cancellation or an output failure.
Operational explanations go to stderr. When analysis returns partial findings,
they remain in the report with issues and a `notEvaluated` gate. Argument/policy
failures before an analysis result exists produce stderr and exit `2` without a
report. Cancellation before publication may also prevent a report; check the exit
code as well as the document. A previously written file is not proof that a later
invocation succeeded.

## JSON version one

The authoritative [JSON Schema](schema/report.v1.schema.json) uses JSON Schema
2020-12. The envelope's integer `schemaVersion` is `1`; incompatible changes require
a new version. Consumers should select the schema by that version and inspect
`status` and `gateOutcome` before treating counts as a compliance signal.

| Field | Meaning |
| --- | --- |
| `status` | `completed`, `incomplete`, `failed` or `cancelled` coverage |
| `gateOutcome` | `passed`, `failed` or `notEvaluated`; only completed coverage has an evaluated gate |
| `baselineApplied` | Whether a usable explicit baseline or Git-base comparison classified this result |
| `baseCommit` | Resolved Git base commit, otherwise `null`; it may be present even if comparison failed |
| `rules` | Ordinal rule metadata: ID, title and documentation URL |
| `diagnostics` | Every available finding, including accepted debt and partial results |
| `resolved` | Accepted entries absent from completed analysis; empty for incomplete, failed or cancelled analysis |
| `issues` | Operational code, message, optional rule ID and optional location |

Each diagnostic contains `id` (snapshot identity), `fingerprint` (F22 semantic
baseline identity), `ruleId`, effective `severity` (`info`, `warning`, `error`),
`message`, `target`, nullable `location`, all `evidence`, `remediation`, and
`documentationUrl`. It also retains `dependencyPath`, violated `constraints`,
`publicContracts`, and the nullable `snapshotId` fallback used by fingerprints.
`baselineState` is `existing` or `new` when a comparison is available, otherwise
`null`. Baseline acceptance does not change severity or hide evidence.

Resolved entries contain their fingerprint, rule, target, dependency path,
constraints and snapshot fallback. Baselines do not store source spans, messages
or effective severities; exports do not fabricate those fields for resolved debt.
See [baseline matching](baselines.md) and [Git comparison](changes.md).

Locations are relative to the policy directory, with normalized forward slashes,
one-based UTF-16 columns, inclusive starts and exclusive ends. Point locations
retain equal start/end coordinates. Absolute paths inside the policy directory are
relativized; escaping or foreign rooted paths cause an operational export failure.
Findings without physical source evidence retain `null` locations.

## SARIF 2.1.0 mapping

The output follows the [OASIS SARIF 2.1.0 schema](https://docs.oasis-open.org/sarif/sarif/v2.1.0/errata01/os/schemas/sarif-schema-2.1.0.json).
An unmodified [vendored copy](schema/sarif-2.1.0.schema.json) supports offline validation;
see its [source attribution](schema/SARIF-SOURCE.md).
There is one run, with tool name `Architecture Guardian`, tool version, repository
information URI and rule metadata under `tool.driver.rules`.

| SARIF field | Architecture Guardian meaning |
| --- | --- |
| `result.ruleId`, `message.text` | Canonical rule identity and actual diagnostic message |
| `result.level` | `info` → `note`; `warning` → `warning`; `error` → `error` |
| `locations` | Primary source span when available; omitted otherwise |
| `relatedLocations` | Located evidence with descriptions; unlocated evidence remains in properties |
| `partialFingerprints["architectureGuardian/v1"]` | Existing versioned F22 fingerprint; reporting does not recompute matching semantics |
| `baselineState` | Existing → `unchanged`; new → `new`; resolved → `absent`; omitted without classification |
| `result.properties` | The complete JSON v1 diagnostic, or resolved baseline entry for an absent result |
| `run.properties` | `schemaVersion: 1`, status, gate outcome, baseline-applied flag, base commit and operational issues |
| `invocations[0].executionSuccessful` | Whether analysis completed, independently of gate success |
| `toolExecutionNotifications` | Operational issues as error notifications, preserving located context |

SARIF `columnKind` is `utf16CodeUnits`. Artifact URIs are repository-relative,
segment-escaped (including spaces, `#`, `%` and Unicode) and use `%SRCROOT%` as their
base identifier. No absolute machine root is embedded. Consumers resolve that base
against their checkout's policy directory. Point ranges omit SARIF end coordinates;
the exact neutral range remains in properties. Unlocated project-reference findings
do not invent a physical source span. Absent results use `level: none` and
`kind: notApplicable`, retaining their original fingerprint and semantic context.
They are emitted only for completed comparison coverage.

## Determinism and validation

Reports preserve Core's canonical ordinal diagnostic, evidence, issue and baseline
ordering, breaking equal snapshot-key ties by the complete serialized diagnostic.
Rules use ordinal ID order. SARIF appends resolved entries after current
findings. The adapter adds no timestamps, random run identifiers or absolute checkout
roots. Messages and evidence are retained verbatim, including any paths supplied
by upstream tools. The same result/context produces byte-identical output regardless
of culture or enumeration order. Different source snapshots can still change diagnostic
IDs; baseline fingerprints have the separate matching semantics described above.

After building Release, run:

```sh
python3 -m pip install jsonschema==4.26.0
python3 eng/test-reports.py
```

Use a virtual environment where required. The script validates real completed,
gate-failing, accepted, resolved, empty and partial CLI reports against JSON v1,
the complete OASIS schema and the versioned properties contract. It also checks
stdout/file equivalence, repeatability and file failures. CI runs it on Linux,
Windows and macOS. Focused xUnit tests cover Git comparison exports, evidence,
severity mappings, URI escaping, cancellation and report-write failures.
