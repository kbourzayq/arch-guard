# Reviewed architecture onboarding with an agent

Use the portable `architecture-guardian-onboarding` skill for first-time discovery
or an intentional review of existing architecture policy. It guides Explore →
Understand → Propose → Review → Validate → Adopt. For ordinary coding under approved
policy, use [architecture-guardian](agent-workflow.md). Direct [manual policy
creation](getting-started.md) and CLI adoption remain supported.

The skill interprets evidence from existing tools; it adds no analysis engine,
architecture format or Architecture Guardian cloud dependency. The 0.4.0 CLI
supports deterministic proposals through [`init --discover`](discovery.md), alongside
manual proposals and validation. The skill checks
installed help and available MCP tools rather than assuming discovery is installed.
MCP currently has no discovery tool.

## Install and invoke

Install the [CLI or MCP server](install.md) separately. Copy the **whole**
[skills/architecture-guardian-onboarding directory](../skills/architecture-guardian-onboarding/SKILL.md),
including `references/`, from a reviewed revision containing it. NuGet does not
install skills. A checkout supplies files only; building tools is optional.

From your target repository root, replace `<ARCH_GUARD_CHECKOUT>` with that checkout.
For Codex on macOS/Linux, this refuses an existing destination:

```sh
mkdir -p .agents/skills
test ! -e .agents/skills/architecture-guardian-onboarding && cp -R "<ARCH_GUARD_CHECKOUT>/skills/architecture-guardian-onboarding" .agents/skills/
```

In PowerShell:

```powershell
New-Item -ItemType Directory -Force .agents/skills
if (Test-Path .agents/skills/architecture-guardian-onboarding) { throw "Skill already installed; review an update separately." }
Copy-Item -Recurse "<ARCH_GUARD_CHECKOUT>/skills/architecture-guardian-onboarding" .agents/skills/
```

For Claude/Copilot replace `.agents` with the table's destination prefix in both
commands and collision checks. Preserve an existing installation, compare its
contents and update deliberately. Start a fresh session if needed and verify the
skill is discovered before asking it to evaluate the repository.

| Harness | Project entry point | Invocation |
| --- | --- | --- |
| Codex | `.agents/skills/architecture-guardian-onboarding/SKILL.md` | `$architecture-guardian-onboarding` |
| Claude Code | `.claude/skills/architecture-guardian-onboarding/SKILL.md` | `/architecture-guardian-onboarding` |
| Compatible Copilot harness (CLI or IDE agent mode with skills support) | `.github/skills/architecture-guardian-onboarding/SKILL.md` | Ask it to use the `architecture-guardian-onboarding` skill |

These paths follow official [Codex skills](https://learn.chatgpt.com/docs/build-skills),
[Claude Code skills](https://code.claude.com/docs/en/skills) and
[Copilot skills](https://docs.github.com/en/copilot/concepts/customizations/agent-skills)
documentation. Client version, tool permissions and organization settings determine
actual discovery and command/MCP availability. Installing the skill grants neither
build trust nor policy-adoption approval. No repository-wide instruction is required;
if adding one, point to the skill and YAML rather than duplicating constraints.
Update by reviewing the installed folder against the desired revision. Remove it
by deleting the copied folder and references in repository instructions.

Try:

> Use architecture-guardian-onboarding to inspect this repository and propose its
> first policy. Explain ownership and evidence, ask about ambiguous roles, compare
> with any existing policy and validate the separate candidate. Present the exact
> adoption diff and enforcement choices before writing the active policy.

## Review the decisions

The agent locates instructions, solutions/projects, policies/imports, architectural
docs and CI contexts. It separates verified inventory and actual dependency evidence
from inferred module/layer/contract ownership and style. Project names alone cannot
approve intent. Unresolved ownership gets a targeted question, not an omitted project
or an invented namespace selector: schema v1 owns whole projects.

Authorize MSBuild tasks/generators before restore or evaluation. Without trust,
static observations stay unevaluated and compliance unresolved. When discovery is
unavailable, the agent authors a separate schema-supported candidate from verified
paths and reviewed intent. It reports discovery unavailable rather than installing
a newer tool or fabricating a discovery report. With authorized discovery it retains
JSON coverage, evidence and questions separately from the proposed YAML and gate.

Review `architecture.guardian.proposed.yaml` and its diff from the approved policy,
including effective imported constraints. Validate that explicit candidate using
`inspect`/`scan` or `list_rules`/`get_architecture`/`analyze_solution` in each required
context. A valid policy or discovery exit `0` does not prove a passing gate.
Classify real findings as debt against approved intent, a proposed policy mistake,
or an operational failure. Authorize corrections separately where necessary.

Choose full enforcement, an explicitly reviewed [baseline](baselines.md), or an
explicit [Git-base gate](changes.md). Existing debt can pass a new-finding gate;
MCP does not persist/read CLI baseline files. Both Git snapshots need policy at
the same path, so a base before first policy adoption cannot provide that comparison.
Do not silently accept debt, lower severity or refresh a baseline to get a pass.

Only after an explicit decision covering the exact candidate, target and enforcement
choice does the agent adopt it. The approved policy is preserved when approval is
absent or declined. Changed inputs require a new review. After adoption, repeat
inspection and full scanning against the active path, then the approved incremental
gate and normal build/tests. Keep an independent CI gate. Missing assets, failed
restore, incomplete/failed/cancelled analysis or zero partial findings never establish
compliance. Record remaining failures even after an approved policy write.

See the copied [workflow reference](../skills/architecture-guardian-onboarding/references/workflow.md)
for exact supported CLI/MCP calls and the
[onboarding report example](../skills/architecture-guardian-onboarding/references/report-example.md)
for decisions, questions, context, rules, status, gate and limitations. That report
is illustrative, not a validation claim for this repository.

## Verification record

On 2026-11-07 the implementation environment reported `codex-cli 0.157.0` on macOS;
only version detection was performed. No authenticated interactive onboarding was
executed in Codex, Claude Code or Copilot, and no tested Claude/Copilot version is
claimed. Harness installation paths were checked as copied directories, not through
each client's skill loader. Record the actual client version and discovery result
when validating your installation; official path compatibility is not client execution.

After a Release solution build, run from the repository root:

```sh
python3 eng/test-onboarding.py
```

The script checks frontmatter, all bundled reference links after copying to the
three project paths and refusal to replace an installed directory. It validates
the bundled YAML example through the strict loader and resolved workspace. It runs real
CLI/MCP contract tests: clean/existing policy manual onboarding, missing restore
assets, candidate inspection, violations, preserved active policy, scripted explicit
adoption, correction and adopted-path revalidation. It reuses deterministic discovery
and proposal fixtures for ambiguous ownership, incomplete/untrusted coverage and
create-only outputs, and the stdio correction fixture for fresh diagnostic evidence.
Manual onboarding bypasses discovery deliberately. To check a separately installed
older binary, also pass `--legacy-cli <FULL_PATH_TO_LEGACY_ARCHGUARD>`; the script
confirms discovery is absent and runs candidate inspect/scan with that executable.
This path was checked with published CLI 0.3.0 in an isolated temporary installation.
The default CI check uses the development build. These checks do not prove an
LLM's capability detection or approval behavior.
They do not execute a client following skill prose. Those boundaries require actual
harness evaluation; a script's developer approval is explicit test input.
CI uses `--portable-only` because its normal test jobs already run the interface
scenarios, avoiding duplicate workspace evaluation.
