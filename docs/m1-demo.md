# M1 reference solutions and demo

[F11](https://github.com/kbourzayq/arch-guard/issues/11) provides two checked-in,
package-free reference solutions under `tests/Fixtures/ReferenceSolutions`.
`CleanArchitecture` contains Domain ports, an Application use case, Infrastructure
implementations and Host composition, all in the Shop module. `ModularMonolith`
contains Billing and Orders modules with separate contracts and application projects.
Orders may consume Billing's public API; it may not consume Billing implementation.
Both policies select AG001, AG002 and AG003 as errors with an error gate.

Named MSBuild configurations select committed source and project-reference changes.
`Compliant` is the negative control; ordinary Debug/Release also use the compliant
sources. All violating configurations except `Incomplete` compile successfully.
No policy edits, generated fixtures or circular project references are needed.

| Solution | Configuration | Exact expected rules | Exit | Expected outcome |
| --- | --- | --- | --- | --- |
| CleanArchitecture | Compliant | none | 0 | Gate passed |
| CleanArchitecture | AG002 | AG002 (one finding) | 1 | Application bypasses its Domain port and uses Infrastructure |
| CleanArchitecture | Incomplete | AG002 (one retained finding) | 2 | MissingType makes semantic coverage incomplete; no gate decision |
| ModularMonolith | Compliant | none | 0 | Gate passed; Orders consumes Billing contracts |
| ModularMonolith | AG001 | AG001 (one finding) | 1 | Orders consumes Billing implementation |
| ModularMonolith | AG003 | AG003 (one finding) | 1 | Billing → Orders → Billing through allowed contract dependencies |
| ModularMonolith | Incomplete | AG001 (one retained finding) | 2 | Unavailable.Assembly is absent; loading is incomplete; no gate decision |

The AG003 case has an acyclic project graph: each application's references point to
independent contract projects. Grouping those projects into logical modules reveals
the cycle. Contract permissions permit both edges for AG001, while AG003 still
rejects the cycle. Applications share a layer, so the AG001 seed does not also
produce AG002. The Clean Architecture seed stays within one module, isolating AG002.

## Run the demo

From the repository root, use the SDK in `global.json` and build the CLI:

```sh
dotnet restore ArchitectureGuardian.slnx --locked-mode
dotnet build ArchitectureGuardian.slnx --configuration Release --no-restore
```

For any buildable row in the table, replace the solution and configuration in this
example. Restore the same configuration that will be scanned; conditional references
are part of that build context. The fixtures have no NuGet dependencies.

```sh
dotnet restore tests/Fixtures/ReferenceSolutions/ModularMonolith/ModularMonolith.slnx -p:Configuration=AG003
dotnet build tests/Fixtures/ReferenceSolutions/ModularMonolith/ModularMonolith.slnx --configuration AG003 --no-restore
dotnet run --project src/ArchitectureGuardian.Cli --configuration Release --no-build -- scan tests/Fixtures/ReferenceSolutions/ModularMonolith/ModularMonolith.slnx --policy tests/Fixtures/ReferenceSolutions/ModularMonolith/architecture.guardian.yaml --configuration AG003 --trusted
```

The final command returns 1 and reports one AG003 diagnostic, source evidence,
remediation and `Gate failed`. Replacing both `AG003` values in the restore/scan
commands with `Compliant` returns 0, zero diagnostics and `Gate passed`.
Use `CleanArchitecture` and configuration `AG002` for the layer violation, or
`ModularMonolith` with `AG001` for the forbidden implementation dependency.
To inspect resolved ownership without running rules:

```sh
dotnet run --project src/ArchitectureGuardian.Cli --configuration Release --no-build -- inspect tests/Fixtures/ReferenceSolutions/ModularMonolith/ModularMonolith.slnx --policy tests/Fixtures/ReferenceSolutions/ModularMonolith/architecture.guardian.yaml --configuration Compliant --trusted
dotnet run --project src/ArchitectureGuardian.Cli --configuration Release --no-build -- explain AG003
```

For either `Incomplete` row, first restore its `Compliant` configuration, then scan
with `--configuration Incomplete`. Do not build the incomplete configurations:
CleanArchitecture intentionally contains an unresolved type, and ModularMonolith
intentionally references an assembly that does not exist. The scan returns 2, retains
the known violation on stdout, and describes incomplete coverage on stderr without claiming either gate outcome. `--trusted`
authorizes MSBuild evaluation of these repository-owned fixture projects.

## Repeatable correctness checks

```sh
dotnet test tests/ArchitectureGuardian.ArchitectureTests/ArchitectureGuardian.ArchitectureTests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~ReferenceSolutionTests
```

`ReferenceSolutionTests` copies the committed fixtures to an isolated directory,
restores the four valid contexts through `Preparation.slnx`, then checks every row
through the reusable API and the real CLI process. The exact diagnostic cardinality
and rule identity reject both false positives and missed violations. Each clean row
is a negative control for all three rules; the three complete violating rows are
known positives for AG001–AG003. This is a small labeled regression corpus, not an
estimate of accuracy across arbitrary repositories.

Checks also pin the offending project pair or cycle, source file and line, semantic
symbols, valid source ranges, retained findings during incomplete coverage, stable
IDs across independent workspace loads, and agreement between API findings and CLI
IDs and exit codes. The tests are tagged `Category=Integration` and run in the normal
CI suite on Linux, Windows and macOS. The fixtures deliberately remain outside the
production solution and its repository policy inventory. Existing dependency-boundary,
formatting and policy tests remain the drift checks; no generated artifact or new
drift framework is required.
