using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Baselines;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;
using ArchitectureGuardian.Reporting;

namespace ArchitectureGuardian.ReportSamples;

/// <summary>Exports neutral report scenarios in one process for the external schema validator.</summary>
internal static class Program
{
    /// <summary>Writes both encodings for every scenario to an explicitly supplied fixture directory.</summary>
    private static async Task Main(string[] args)
    {
        if (args.Length != 1) { throw new ArgumentException("Supply the schema sample output directory."); }
        var directory = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(directory);
        var existing = Finding("Existing", DiagnosticSeverity.Error);
        var added = Finding("New", DiagnosticSeverity.Error);
        var removed = Finding("Resolved", DiagnosticSeverity.Error);
        var baseline = BaselineSnapshot.Capture(new(AnalysisStatus.Completed, [existing, removed]));
        var scenarios = new (string Name, AnalysisResult Analysis, bool? Gate, bool Compared)[]
        {
            ("clean", new(AnalysisStatus.Completed), true, false),
            ("findings", new(AnalysisStatus.Completed, [Finding("Info", DiagnosticSeverity.Info), Finding("Warning", DiagnosticSeverity.Warning)]), true, false),
            ("gate-failed", new(AnalysisStatus.Completed, [added]), false, false),
            ("accepted", new(AnalysisStatus.Completed, [existing]), true, true),
            ("baseline", new(AnalysisStatus.Completed, [existing, added]), false, true),
            ("resolved", new(AnalysisStatus.Completed), true, true),
            ("reference", new(AnalysisStatus.Completed, [Finding("Reference", DiagnosticSeverity.Error, located: false)]), false, false),
            ("incomplete", new(AnalysisStatus.Incomplete, [existing, added], [new("missing-context", "Fixture coverage unavailable", "AG001", new("App/Issue.cs", 1, 1))]), null, true),
            ("failed", new(AnalysisStatus.Failed, [existing], [new("fixture-failure", "Fixture analysis failed")]), null, true),
            ("cancelled", new(AnalysisStatus.Cancelled, [existing]), null, true),
        };
        foreach (var (name, analysis, gate, compared) in scenarios)
        {
            var comparison = compared ? new BaselineComparison(analysis, baseline) : null;
            foreach (var format in new[] { ReportFormat.Json, ReportFormat.Sarif })
            {
                var report = ReportSerializer.Serialize(analysis, format, directory, gate, comparison,
                    compared ? new string('a', 40) : null);
                await ReportWriter.WriteAsync(Path.Combine(directory, name + "." + format.ToString().ToLowerInvariant()), report);
            }
        }
    }

    /// <summary>Retains physical and unlocated evidence, semantic identity and remediation in every finding.</summary>
    private static Diagnostic Finding(string symbol, DiagnosticSeverity severity, bool located = true)
    {
        var source = OperatingSystem.IsWindows() ? "App/A #é%.cs" : "App/A #é%:Part.cs";
        SourceLocation? location = located ? new(source, 2, 3, 2, 8) : null;
        return new(new RuleViolation(RuleCatalog.All.Single(rule => rule.Id == "AG001"), "Forbidden dependency: " + symbol,
            new("App", symbol), [new("Project reference"), new("Bound symbol", location)], "Use a declared contract.", location,
            new([new("project", "App/App.csproj"), new("project", "Library/Library.csproj")],
                [new("modules.App", "allowedDependencies", [])], ["contract:Library/api"])), severity);
    }
}
