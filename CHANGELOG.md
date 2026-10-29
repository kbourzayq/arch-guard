## v0.3.0 (2026-10-29)

### Feat

- **performance**: add benchmark budgets and opt-in local telemetry
- **policy**: share YAML and enable repository validation
- **agents**: add architecture skill and installation-first guides

### Fix

- **analyzers**: inspect function-pointer aliases for MVP release

## v0.2.0 (2026-10-27)

### Feat

- **distribution**: add verified NuGet packaging and releases
- **ci**: add independent architecture quality gates (#79)
- **reporting**: export versioned JSON and SARIF reports
- **analysis**: add full-context Git change analysis
- **baselines**: add stable fingerprints and new-violation gates
- **mcp**: explain violations with evidence and remediation (#65)
- **mcp**: expose solution and change analysis tools
- **mcp**: expose architecture and rule context (#63)
- **mcp**: add read-only stdio server (#62)
- **rules**: enforce domain infrastructure references
- **rules**: enforce forbidden assembly and namespace references (#59)
- **rules**: enforce cross-module implementation boundaries
- **analyzers**: add compiler adapter and semantic test harness
- **skills**: add reviewed issue-to-PR workflow
- **cli**: add policy-driven scan inspect and explain commands
- **rules**: detect AG003 module dependency cycles
- **rules**: enforce AG002 layer dependencies
- **rules**: enforce AG001 module dependencies
- **analysis**: extract dependency edges and evidence graph
- **analysis**: implement F05 normalized repository model
- **analysis**: implement F04 workspace loading
- **configuration**: implement F03 versioned YAML policy
- **core**: define shared rule and analysis contracts
- **core**: establish .NET solution and component boundaries

### Fix

- **analysis**: preserve nameof and deconstruction semantics
- **tests**: stabilize workspace fixtures across SDKs and Windows
- **analysis**: preserve MSBuild context and generator diagnostics
- **cli**: use async output and own console cancellation
- **ci**: preserve LF line endings on Windows
