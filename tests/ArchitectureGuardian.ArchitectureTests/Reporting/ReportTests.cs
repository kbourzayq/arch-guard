using System.Globalization;
using System.Text.Json;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Baselines;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;
using ArchitectureGuardian.Reporting;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Reporting;

/// <summary>Verifies report contracts independently of workspace loading and host presentation.</summary>
public sealed class ReportTests
{
    /// <summary>Reports are byte-stable across enumeration orders and cultures, with all evidence and metadata intact.</summary>
    /// <param name="format">The report encoding.</param>
    [Theory]
    [InlineData(ReportFormat.Json)]
    [InlineData(ReportFormat.Sarif)]
    public void Reports_preserve_findings_severities_and_portable_locations(ReportFormat format)
    {
        var located = Finding("Located", DiagnosticSeverity.Info, new("src/./A #é%.cs", 2, 3, 2, 8));
        var reference = Finding("Reference", DiagnosticSeverity.Warning);
        var first = new AnalysisResult(AnalysisStatus.Completed, [located, reference]);
        var second = new AnalysisResult(AnalysisStatus.Completed, [reference, located]);
        var before = CultureInfo.CurrentCulture;
        try
        {
            var text = ReportSerializer.Serialize(first, format, Path.GetTempPath(), true);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal(text, ReportSerializer.Serialize(second, format, Path.GetTempPath(), true));
            Assert.EndsWith("\n", text);
            Assert.DoesNotContain("\r", text);
            using var document = JsonDocument.Parse(text);
            var envelope = Envelope(document, format);
            Assert.Equal("completed", envelope.GetProperty("status").GetString());
            Assert.Equal("passed", envelope.GetProperty("gateOutcome").GetString());
            Assert.False(envelope.GetProperty("baselineApplied").GetBoolean());
            var entries = Diagnostics(document, format).ToArray();
            Assert.Equal(2, entries.Length);
            foreach (var expected in first.Diagnostics)
            {
                var entry = entries.Single(item => item.GetProperty("id").GetString() == expected.Id);
                Assert.Equal(expected.Message, entry.GetProperty("message").GetString());
                Assert.Equal(expected.Remediation, entry.GetProperty("remediation").GetString());
                Assert.Equal(expected.Severity.ToString().ToLowerInvariant(), entry.GetProperty("severity").GetString());
                Assert.Equal(BaselineEntry.FromDiagnostic(expected).Fingerprint, entry.GetProperty("fingerprint").GetString());
                Assert.Equal(2, entry.GetProperty("evidence").GetArrayLength());
                Assert.Single(entry.GetProperty("publicContracts").EnumerateArray());
                Assert.Equal(JsonValueKind.Null, entry.GetProperty("baselineState").ValueKind);
            }
            var source = entries.Single(item => item.GetProperty("id").GetString() == located.Id).GetProperty("location");
            Assert.Equal("src/A #é%.cs", source.GetProperty("path").GetString());
            Assert.Equal(8, source.GetProperty("endColumn").GetInt32());
            if (format == ReportFormat.Sarif)
            {
                var run = document.RootElement.GetProperty("runs")[0];
                Assert.Equal("2.1.0", document.RootElement.GetProperty("version").GetString());
                Assert.True(run.GetProperty("invocations")[0].GetProperty("executionSuccessful").GetBoolean());
                var result = run.GetProperty("results").EnumerateArray().Single(item => item.GetProperty("level").GetString() == "note");
                Assert.Equal("src/A%20%23%C3%A9%25.cs", result.GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString());
                Assert.Single(result.GetProperty("relatedLocations").EnumerateArray());
                var unlocated = run.GetProperty("results").EnumerateArray().Single(item => item.GetProperty("level").GetString() == "warning");
                Assert.False(unlocated.TryGetProperty("locations", out _));
                Assert.Equal(6, run.GetProperty("tool").GetProperty("driver").GetProperty("rules").GetArrayLength());
            }
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    /// <summary>Existing and new debt stays visible; only complete coverage may report absent baseline entries.</summary>
    /// <param name="format">The report encoding.</param>
    /// <param name="status">The analysis coverage outcome.</param>
    [Theory]
    [InlineData(ReportFormat.Json, AnalysisStatus.Completed)]
    [InlineData(ReportFormat.Sarif, AnalysisStatus.Completed)]
    [InlineData(ReportFormat.Json, AnalysisStatus.Incomplete)]
    [InlineData(ReportFormat.Sarif, AnalysisStatus.Incomplete)]
    [InlineData(ReportFormat.Json, AnalysisStatus.Failed)]
    [InlineData(ReportFormat.Sarif, AnalysisStatus.Failed)]
    [InlineData(ReportFormat.Json, AnalysisStatus.Cancelled)]
    [InlineData(ReportFormat.Sarif, AnalysisStatus.Cancelled)]
    public void Baselines_and_coverage_remain_independent(ReportFormat format, AnalysisStatus status)
    {
        var existing = Finding("Existing", DiagnosticSeverity.Error);
        var added = Finding("New", DiagnosticSeverity.Warning);
        var removed = Finding("Removed", DiagnosticSeverity.Error);
        var baseline = BaselineSnapshot.Capture(new(AnalysisStatus.Completed, [existing, removed]));
        var analysis = new AnalysisResult(status, [added, existing], status == AnalysisStatus.Completed ? [] :
            [new("missing-context", "Workspace coverage unavailable", "AG001", new("App/A.cs", 1, 1))]);
        var comparison = new BaselineComparison(analysis, baseline);
        using var document = JsonDocument.Parse(ReportSerializer.Serialize(analysis, format, Path.GetTempPath(),
            analysis.IsComplete ? false : null, comparison, new string('a', 40)));
        var envelope = Envelope(document, format);
        Assert.Equal(status.ToString().ToLowerInvariant(), envelope.GetProperty("status").GetString());
        Assert.Equal(analysis.IsComplete ? "failed" : "notEvaluated", envelope.GetProperty("gateOutcome").GetString());
        Assert.True(envelope.GetProperty("baselineApplied").GetBoolean());
        Assert.Equal(new string('a', 40), envelope.GetProperty("baseCommit").GetString());
        Assert.Equal(analysis.IsComplete ? 0 : 1, envelope.GetProperty("issues").GetArrayLength());
        var diagnostics = Diagnostics(document, format).ToArray();
        Assert.Equal("existing", diagnostics.Single(item => item.GetProperty("id").GetString() == existing.Id).GetProperty("baselineState").GetString());
        Assert.Equal("new", diagnostics.Single(item => item.GetProperty("id").GetString() == added.Id).GetProperty("baselineState").GetString());
        if (format == ReportFormat.Json)
        {
            Assert.Equal(analysis.IsComplete ? 1 : 0, document.RootElement.GetProperty("resolved").GetArrayLength());
        }
        else
        {
            var run = document.RootElement.GetProperty("runs")[0];
            Assert.Equal(analysis.IsComplete, run.GetProperty("invocations")[0].GetProperty("executionSuccessful").GetBoolean());
            Assert.Equal(analysis.IsComplete ? 3 : 2, run.GetProperty("results").GetArrayLength());
            Assert.Contains(run.GetProperty("results").EnumerateArray(), item => item.GetProperty("baselineState").GetString() == "unchanged");
            if (analysis.IsComplete)
            {
                var absent = run.GetProperty("results")[2];
                Assert.Equal("absent", absent.GetProperty("baselineState").GetString());
                Assert.Equal(BaselineEntry.FromDiagnostic(removed).Fingerprint, absent.GetProperty("partialFingerprints").GetProperty("architectureGuardian/v1").GetString());
            }
        }
    }

    /// <summary>Equal snapshot IDs do not merge baseline states when evaluator context distinguishes fingerprints.</summary>
    /// <param name="format">The report encoding.</param>
    [Theory]
    [InlineData(ReportFormat.Json)]
    [InlineData(ReportFormat.Sarif)]
    public void Baseline_membership_does_not_merge_distinct_contexts_with_shared_snapshot_ids(ReportFormat format)
    {
        var accepted = Finding("Shared", DiagnosticSeverity.Error);
        var changed = new Diagnostic(new RuleViolation(accepted.Violation.Rule, accepted.Message, accepted.Target,
            accepted.Evidence, accepted.Remediation, accepted.Location,
            new(accepted.Violation.Context!.DependencyPath, [new("modules.App", "allowedDependencies", ["module:Other"])])),
            DiagnosticSeverity.Error);
        Assert.Equal(accepted.Id, changed.Id);
        var acceptedEntry = BaselineEntry.FromDiagnostic(accepted);
        var changedEntry = BaselineEntry.FromDiagnostic(changed);
        Assert.NotEqual(acceptedEntry.Fingerprint, changedEntry.Fingerprint);
        var analysis = new AnalysisResult(AnalysisStatus.Completed, [accepted, changed]);
        var comparison = new BaselineComparison(analysis, BaselineSnapshot.Capture(new(AnalysisStatus.Completed, [accepted])));
        Assert.Same(accepted, Assert.Single(comparison.Existing));
        Assert.Same(changed, Assert.Single(comparison.New));
        var text = ReportSerializer.Serialize(analysis, format, Path.GetTempPath(), false, comparison);
        var reversed = new AnalysisResult(AnalysisStatus.Completed, [changed, accepted]);
        var reversedComparison = new BaselineComparison(reversed, BaselineSnapshot.Capture(new(AnalysisStatus.Completed, [accepted])));
        Assert.Equal(text, ReportSerializer.Serialize(reversed, format, Path.GetTempPath(), false, reversedComparison));
        using var document = JsonDocument.Parse(text);
        var diagnostics = Diagnostics(document, format).ToArray();
        Assert.Equal("existing", diagnostics.Single(item => item.GetProperty("fingerprint").GetString() == acceptedEntry.Fingerprint)
            .GetProperty("baselineState").GetString());
        Assert.Equal("new", diagnostics.Single(item => item.GetProperty("fingerprint").GetString() == changedEntry.Fingerprint)
            .GetProperty("baselineState").GetString());
    }

    /// <summary>Colons in legal Unix filenames remain source evidence and are escaped as URI data.</summary>
    [Fact]
    public void Unix_colon_filenames_remain_repository_relative()
    {
        if (OperatingSystem.IsWindows()) { return; } // This source filename is specific to Unix filesystems.
        var analysis = new AnalysisResult(AnalysisStatus.Completed,
            [Finding("Colon", DiagnosticSeverity.Error, new("src/Part:One.cs", 1, 1))]);
        using var json = JsonDocument.Parse(ReportSerializer.Serialize(analysis, ReportFormat.Json, Path.GetTempPath(), false));
        Assert.Equal("src/Part:One.cs", json.RootElement.GetProperty("diagnostics")[0].GetProperty("location").GetProperty("path").GetString());
        using var sarif = JsonDocument.Parse(ReportSerializer.Serialize(analysis, ReportFormat.Sarif, Path.GetTempPath(), false));
        Assert.Equal("src/Part%3AOne.cs", sarif.RootElement.GetProperty("runs")[0].GetProperty("results")[0]
            .GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString());
    }

    /// <summary>Invalid context and escaping paths fail explicitly rather than producing misleading output.</summary>
    [Fact]
    public void Invalid_report_context_is_rejected()
    {
        var complete = new AnalysisResult(AnalysisStatus.Completed);
        Assert.Throws<ArgumentException>(() => ReportSerializer.Serialize(complete, ReportFormat.Json, Path.GetTempPath(), null));
        var incomplete = new AnalysisResult(AnalysisStatus.Incomplete, issues: [new("gap", "Missing context")]);
        Assert.Throws<ArgumentException>(() => ReportSerializer.Serialize(incomplete, ReportFormat.Json, Path.GetTempPath(), true));
        var comparison = new BaselineComparison(complete, BaselineSnapshot.Capture(complete));
        Assert.Throws<ArgumentException>(() => ReportSerializer.Serialize(new(AnalysisStatus.Completed), ReportFormat.Json, Path.GetTempPath(), true, comparison));
        foreach (var path in new[] { "../outside.cs", "C:/outside.cs", "//server/share/outside.cs" })
        {
            Assert.Throws<ArgumentException>(() => ReportSerializer.Serialize(new(AnalysisStatus.Completed,
                [Finding("Outside", DiagnosticSeverity.Error, new(path, 1, 1))]), ReportFormat.Sarif, Path.GetTempPath(), false));
        }
    }

    /// <summary>Replacement, cancellation and write errors leave no partial report or temporary files.</summary>
    [Fact]
    public async Task File_publication_is_atomic_and_failures_are_visible()
    {
        var root = Path.Combine(Path.GetTempPath(), "archguard-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "report.json");
            await File.WriteAllTextAsync(path, "old", TestContext.Current.CancellationToken);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReportWriter.WriteAsync(path, "replacement", new CancellationToken(true)));
            Assert.Equal("old", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            await ReportWriter.WriteAsync(path, "{\"schemaVersion\":1}\n", TestContext.Current.CancellationToken);
            Assert.StartsWith("{", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            var directory = Path.Combine(root, "destination");
            Directory.CreateDirectory(directory);
            // Replacing a directory reports access denied on Windows and an I/O error on Unix.
            var failure = await Record.ExceptionAsync(() => ReportWriter.WriteAsync(directory, "new", TestContext.Current.CancellationToken));
            Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
            Assert.True(Directory.Exists(directory));
            await Assert.ThrowsAnyAsync<IOException>(() => ReportWriter.WriteAsync(Path.Combine(root, "missing", "report"), "new", TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
            Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    private static Diagnostic Finding(string symbol, DiagnosticSeverity severity, SourceLocation? location = null) => new(
        new RuleViolation(RuleCatalog.All.Single(rule => rule.Id == "AG001"), "Forbidden dependency: " + symbol,
            new("App", symbol), [new("Project reference"), new("Bound symbol", location)], "Use a declared contract.", location,
            new([new("project", "App/App.csproj"), new("project", "Library/Library.csproj")],
                [new("modules.App", "allowedDependencies", [])], ["contract:Library/api"])), severity);

    private static JsonElement Envelope(JsonDocument document, ReportFormat format) => format == ReportFormat.Json
        ? document.RootElement : document.RootElement.GetProperty("runs")[0].GetProperty("properties");

    private static IEnumerable<JsonElement> Diagnostics(JsonDocument document, ReportFormat format) => format == ReportFormat.Json
        ? document.RootElement.GetProperty("diagnostics").EnumerateArray()
        : document.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Where(item => !item.TryGetProperty("baselineState", out var state) || state.GetString() != "absent")
            .Select(item => item.GetProperty("properties"));
}
