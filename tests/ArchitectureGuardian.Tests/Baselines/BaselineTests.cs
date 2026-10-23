using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Baselines;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Rules;
using Xunit;

namespace ArchitectureGuardian.Tests.Baselines;

/// <summary>Locks movement, rename, ambiguity and coverage behavior of semantic baseline matching.</summary>
public sealed class BaselineTests
{
    /// <summary>Moving source and changing rendered text or severity preserves accepted semantic debt.</summary>
    [Fact]
    public void Movement_wording_evidence_and_severity_do_not_change_semantic_fingerprint()
    {
        var original = Finding();
        var moved = Finding(path: "Moved/Renamed.cs", line: 50, wording: "Reworded", severity: DiagnosticSeverity.Warning);
        Assert.NotEqual(original.Id, moved.Id);
        Assert.Equal(BaselineEntry.FromDiagnostic(original).Fingerprint, BaselineEntry.FromDiagnostic(moved).Fingerprint);
        var comparison = new BaselineComparison(Completed(moved), BaselineSnapshot.Capture(Completed(original)));
        Assert.Same(moved, Assert.Single(comparison.Existing));
        Assert.Empty(comparison.New);
        Assert.Empty(comparison.Resolved);
        Assert.True(new GatePolicy(DiagnosticSeverity.Warning).PassesNewFindings(comparison));
        Assert.False(new GatePolicy(DiagnosticSeverity.Warning).Passes(comparison.Analysis));
    }

    /// <summary>Renames and changed rule/context identities require explicit acceptance rather than fuzzy suppression.</summary>
    /// <param name="change">The semantic field changed in the fixture.</param>
    [Theory]
    [InlineData("symbol")]
    [InlineData("module")]
    [InlineData("project")]
    [InlineData("rule")]
    [InlineData("constraint")]
    [InlineData("direction")]
    public void Renames_and_changed_semantics_are_new_and_resolved(string change)
    {
        var current = Finding(symbol: change == "symbol" ? "Library.Renamed" : "Library.Contract",
            module: change == "module" ? "Renamed" : "App", project: change == "project" ? "Moved/App.csproj" : "App/App.csproj",
            rule: change == "rule" ? RuleCatalog.DomainInfrastructure : null,
            restriction: change == "constraint" ? "Library.Contract" : "Library", reverse: change == "direction");
        var comparison = new BaselineComparison(Completed(current), BaselineSnapshot.Capture(Completed(Finding())));
        Assert.Same(current, Assert.Single(comparison.New));
        Assert.Single(comparison.Resolved);
        Assert.Empty(comparison.Existing);
        Assert.False(new GatePolicy().PassesNewFindings(comparison));
    }

    /// <summary>Neither side of an ambiguous fingerprint group can silently absorb another occurrence.</summary>
    /// <param name="baselineDuplicates">Whether the accepted side contains duplicate identities.</param>
    /// <param name="currentDuplicates">Whether current findings have duplicate semantic identities.</param>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Ambiguous_matches_remain_new(bool baselineDuplicates, bool currentDuplicates)
    {
        var first = Finding();
        var second = Finding(path: "Other.cs");
        var baseline = BaselineSnapshot.Capture(baselineDuplicates ? Completed(first, second) : Completed(first));
        var comparison = new BaselineComparison(currentDuplicates ? Completed(first, second) : Completed(first), baseline);
        Assert.Empty(comparison.Existing);
        Assert.Equal(currentDuplicates ? 2 : 1, comparison.New.Count);
        Assert.Empty(comparison.Resolved);
        Assert.False(new GatePolicy().PassesNewFindings(comparison));
    }

    /// <summary>Incomplete, failed and cancelled work cannot pass, update debt or establish resolutions.</summary>
    /// <param name="status">The operational outcome.</param>
    [Theory]
    [InlineData(AnalysisStatus.Incomplete)]
    [InlineData(AnalysisStatus.Failed)]
    [InlineData(AnalysisStatus.Cancelled)]
    public void Noncompleted_analysis_never_passes_or_updates_baseline(AnalysisStatus status)
    {
        var diagnostic = Finding();
        var result = new AnalysisResult(status, [diagnostic], [new("load-failed", "Project unavailable")]);
        var baseline = BaselineSnapshot.Capture(Completed(diagnostic, Finding(symbol: "Other")));
        var comparison = new BaselineComparison(result, baseline);
        Assert.Single(comparison.Existing);
        Assert.Empty(comparison.Resolved);
        Assert.Same(result, comparison.Analysis);
        Assert.False(new GatePolicy().PassesNewFindings(comparison));
        Assert.Throws<InvalidOperationException>(() => BaselineSnapshot.Capture(result));
    }

    /// <summary>Existing errors stay visible while only new severities determine a complete gate.</summary>
    [Fact]
    public void New_findings_use_current_inclusive_severity_threshold()
    {
        var existing = Finding();
        var warning = Finding(symbol: "New", severity: DiagnosticSeverity.Warning);
        var baseline = BaselineSnapshot.Capture(Completed(existing, Finding(symbol: "Resolved")));
        var comparison = new BaselineComparison(Completed(existing, warning), baseline);
        Assert.Single(comparison.Existing);
        Assert.Single(comparison.New);
        Assert.Single(comparison.Resolved);
        Assert.True(new GatePolicy().PassesNewFindings(comparison));
        Assert.False(new GatePolicy(DiagnosticSeverity.Warning).PassesNewFindings(comparison));
    }

    /// <summary>Findings without structured context use conservative snapshot identity matching.</summary>
    [Fact]
    public void Missing_context_requires_exact_snapshot_match()
    {
        var first = Finding(withContext: false);
        var moved = Finding(withContext: false, path: "Moved.cs");
        var baseline = BaselineSnapshot.Capture(Completed(first));
        Assert.Single(new BaselineComparison(Completed(first), baseline).Existing);
        Assert.Single(new BaselineComparison(Completed(moved), baseline).New);
        Assert.Equal(first.Id, Assert.Single(baseline.Entries).SnapshotId);
    }

    /// <summary>Version-one fingerprints have a fixed reference independent of runtime hash randomization.</summary>
    [Fact]
    public void Fingerprint_and_collections_are_stable_and_immutable()
    {
        var entry = BaselineEntry.FromDiagnostic(Finding());
        Assert.Equal("3f9afc9dc64ecd0404159333f5acfcde94dc5cb641ec7f5aba90e37d21c91861", entry.Fingerprint);
        Assert.Throws<NotSupportedException>(() => ((IList<ViolationPathNode>)entry.DependencyPath).Clear());
        var entries = new List<BaselineEntry> { entry };
        var snapshot = new BaselineSnapshot(entries);
        entries.Clear();
        Assert.Single(snapshot.Entries);
        Assert.Throws<NotSupportedException>(() => ((IList<BaselineEntry>)snapshot.Entries).Clear());
        var reordered = new BaselineEntry(entry.RuleId, entry.Target, entry.DependencyPath,
            entry.Constraints.Reverse());
        Assert.Equal(entry.Fingerprint, reordered.Fingerprint);
    }

    internal static AnalysisResult Completed(params Diagnostic[] diagnostics) => new(AnalysisStatus.Completed, diagnostics);

    internal static Diagnostic Finding(string path = "App/Consumer.cs", int line = 1, string wording = "Forbidden dependency",
        DiagnosticSeverity severity = DiagnosticSeverity.Error, string symbol = "Library.Contract", string module = "App",
        string project = "App/App.csproj", RuleDescriptor? rule = null, string restriction = "Library", bool reverse = false,
        bool withContext = true)
    {
        var location = new SourceLocation(path, line, 1);
        ViolationPathNode[] endpoints = [new("project", project), new("symbol", symbol)];
        var context = withContext ? new ViolationContext(reverse ? endpoints.Reverse() : endpoints,
            [new("modules." + module, "forbiddenNamespaces", [restriction])]) : null;
        return new(new RuleViolation(rule ?? RuleCatalog.ForbiddenReferences, wording, new(module, symbol),
            [new(wording, location)], "Remove dependency", location, context), severity);
    }
}
