# Manually validate this checkout

The repository's `architecture.guardian.yaml` selects AG001–AG006 as errors for
CLI/MCP. The current compiler analyzer reads that same file and runs AG001, AG002,
AG004, AG005 and AG006. AG003 cycle detection requires the complete solution graph
and runs in CLI/MCP. Compiler feedback alone does not establish six-rule coverage.

Production and its white-box tests belong to the Guardian module, with separate
production/Tests layers enforcing dependency direction. Core bans compiler, build,
YAML and network adapters through AG005 and data-access/ORM namespaces through
AG006. There are no implicit technology bans outside the configured scopes.

## Run the current implementation

From the repository root with its pinned SDK and Python 3 installed:

```sh
dotnet restore ArchitectureGuardian.slnx --locked-mode
python3 eng/validate-repository.py
```

Use `--configuration Debug` to select Debug instead of Release. The script builds
the current analyzer, stages its three DLLs under ignored `artifacts/self-analysis`,
rebuilds the solution with compiler validation enabled, and runs the CLI's complete
six-rule scan. It uses the checkout's code; no published analyzer or CLI installation
is needed. A successful run ends with completed coverage and a passed gate.
CI runs this same workflow in the architecture job on Windows, Linux and macOS.

The stable DLL copy avoids a build dependency cycle and permits the analyzer's own
project to be checked without overwriting a loaded assembly on Windows. Rerun the
script after changing analyzer code. After staging, repeat compiler feedback with:

```sh
dotnet build ArchitectureGuardian.slnx --configuration Release --no-restore --no-incremental -p:ArchitectureGuardianSelfAnalysis=true -p:UseSharedCompilation=false
```

Run just the CLI gate with:

```sh
dotnet src/ArchitectureGuardian.Cli/bin/Release/net10.0/ArchitectureGuardian.Cli.dll scan ArchitectureGuardian.slnx --policy architecture.guardian.yaml --configuration Release --trusted
```

CLI exit 0 means complete/pass, 1 means complete/policy failure, and 2 means an
operational failure or incomplete coverage. `--trusted` allows this repository's
MSBuild evaluation; it does not provide isolation. Normal builds omit self-analysis
unless the property is explicitly enabled. The script stops if compiler validation
fails, so run the CLI command separately when comparing both sets of findings.

## Try a deliberate violation

Create a temporary file `src/ArchitectureGuardian.Core/ManualArchitectureViolation.cs`:

```csharp
namespace ArchitectureGuardian.Core;

internal sealed class ManualArchitectureViolation
{
    internal System.Net.Http.HttpClient? Network { get; }
    internal System.Data.Common.DbConnection? Database { get; }
}
```

The compiler command should report AG005 for network access and AG006 for data
access in Core. The CLI command should complete with the same rule IDs and exit 1.
Remove the temporary file and rerun both commands to return to a passing gate.
Do not suppress diagnostics or change policy to make the negative case pass.

For all six seeded rule scenarios, run the existing reference-solution and shared
semantic parity tests:

```sh
dotnet test tests/ArchitectureGuardian.ArchitectureTests --configuration Release --no-restore --filter FullyQualifiedName~ReferenceSolutionTests
dotnet test tests/ArchitectureGuardian.Tests --configuration Release --no-restore --filter FullyQualifiedName~SemanticAccuracyParityTests
```

The reference solutions exercise module/layer/cycle failures. Shared semantic
fixtures exercise module/layer/implementation/reference restrictions across both
engines and build contexts. See [the rule catalog](rule-catalog.md) for individual
rule triggers and [compiler scope](compiler-analyzer.md#reporting-and-scope) for
differences in diagnostic counts and locations.
