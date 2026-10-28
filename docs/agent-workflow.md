# Architecture-aware agent workflow

Install the optional `architecture-guardian` skill when a local coding agent should
inspect policy before editing and validate its corrections afterward. The skill
uses existing MCP/CLI APIs; it adds no rule engine or automatic enforcement.
Keep independent [CI quality gates](ci.md) and normal build/tests.

## Install the skill

First install the [CLI or MCP server](../README.md) and configure a complete YAML
policy for your target solution. MCP requires a client with local stdio support;
CLI fallback requires ordinary command execution. Authorize the target's build
logic before restore or workspace analysis.

The source is [skills/architecture-guardian](../skills/architecture-guardian/SKILL.md).
Copy that whole folder from a reviewed repository revision containing it; the
original NuGet release predates the skill. NuGet packages do not install agent
instructions. A source checkout is needed only to obtain these files, not to build
the tools.

From **your target repository root**, replace `<ARCH_GUARD_CHECKOUT>` with the path
to a checkout containing the skill. For Codex on macOS/Linux:

```sh
mkdir -p .agents/skills
cp -R "<ARCH_GUARD_CHECKOUT>/skills/architecture-guardian" .agents/skills/
```

In PowerShell:

```powershell
New-Item -ItemType Directory -Force .agents/skills
Copy-Item -Recurse "<ARCH_GUARD_CHECKOUT>/skills/architecture-guardian" .agents/skills/
```

Choose the destination for your harness:

| Harness | Project destination | Invoke |
| --- | --- | --- |
| Codex | `.agents/skills/architecture-guardian/SKILL.md` | `$architecture-guardian` in the prompt |
| Claude Code | `.claude/skills/architecture-guardian/SKILL.md` | `/architecture-guardian` |
| Copilot CLI | `.github/skills/architecture-guardian/SKILL.md` | Ask Copilot to use the `architecture-guardian` skill |

Change the directory in the copy commands for Claude or Copilot. Preserve an existing
skill of the same name; compare and update it deliberately rather than nesting a
second folder inside it. Start a new agent session after installation if needed,
verify discovery, and check that MCP tools or the CLI are available. Installing a
skill does not register MCP or grant workspace execution permission.

These examples follow the official [Codex skill documentation](https://learn.chatgpt.com/docs/build-skills),
[Claude Code skills](https://code.claude.com/docs/en/skills) and
[Copilot skills](https://docs.github.com/en/copilot/concepts/customizations/agent-skills).
Client versions and organization settings can affect discovery. These setup
instructions are distinct from authenticated interactive verification in each client.

## Integrate repository instructions

Merge a small workflow instruction into your target repository's `AGENTS.md`:

```markdown
Use the architecture-guardian skill for changes affecting .NET dependencies.
Read architecture.guardian.yaml as the architecture source of truth. Discover the
solution and required build contexts from our CI configuration. Inspect ownership
before editing, validate after changes, correct relevant findings and report both
coverage and gate outcome. Keep our normal build, tests and independent CI gate.
```

For Claude, place the same instruction in `CLAUDE.md`; for Copilot, use its
repository instructions such as `.github/copilot-instructions.md`. Do not duplicate
module/layer permissions, rule severities or public-contract declarations there.
Read those values from YAML. If compiler integration is enabled, maintain its
shared YAML policy and its [build metadata setup](compiler-analyzer.md). During
the XML transition, synchronize any still-configured legacy snapshot with YAML.

## Run the workflow

For example, ask:

> Use the architecture-guardian skill to implement the requested billing change.
> Inspect our architecture policy first, keep our existing build context, correct
> any new boundary violations and report validation alongside our normal tests.

The skill discovers input and policy paths, configuration, framework, trust and
restore prerequisites. With MCP it reads `list_rules`, resolves `get_architecture`,
then scans with `analyze_solution`. With CLI it uses `inspect` and `scan --format json`.
The agent implements the task through normal file tools, validates, explains actual
findings, corrects relevant dependencies and rescans.

For a concrete end-to-end case, use the checked-in
[correction fixture](../tests/Fixtures/AgentCorrection/Correction.slnx) and the
[MCP correction walkthrough](mcp.md#coding-agent-correction-workflow). The seeded
consumer depends on another module's implementation. The correction uses the
existing contract and removes that implementation project reference, leaving
policy intact. Restore after the project edit, then require completed coverage
and a passing fresh scan.

CLI fallback can inspect the same fixture from the Architecture Guardian checkout
root after installing `archguard`:

```sh
dotnet restore tests/Fixtures/AgentCorrection/Correction.slnx -p:Configuration=Release
archguard inspect tests/Fixtures/AgentCorrection/Correction.slnx --policy tests/Fixtures/AgentCorrection/architecture.guardian.yaml --configuration Release --trusted
archguard scan tests/Fixtures/AgentCorrection/Correction.slnx --policy tests/Fixtures/AgentCorrection/architecture.guardian.yaml --configuration Release --trusted --format json
archguard explain AG001
```

The initial scan completes with a failed gate and one AG001 finding (exit `1`).
The fixture is intentionally violating: experiment in a disposable copy, preserving
its complete folder and build files, rather than committing a corrected fixture.
CLI `explain` provides rule metadata; only MCP `explain_violation` resolves an exact
current diagnostic ID. Old IDs may become stale after corrections.

## Interpret validation honestly

- A full scan must complete before its gate is meaningful. CLI exit `2`, MCP
  incomplete/failed coverage, cancellation and missing trust remain unresolved,
  even when no diagnostics are returned.
- Keep configuration/framework stable and cover every context required by CI.
  Explain blockers such as missing SDKs, restore assets or unavailable Git bases.
- A passing new-finding gate may retain existing errors. Report existing debt
  separately; do not silently update baselines, disable rules or narrow input.
- The agent reports input/policy/build context, completion, gate outcome, corrected
  and remaining findings, operational issues and build/test results. Local scans
  do not prove that hosted CI passed.

Update an installed skill by reviewing and replacing its copied folder. To remove
it, delete that folder and update any repository instructions that reference it.
