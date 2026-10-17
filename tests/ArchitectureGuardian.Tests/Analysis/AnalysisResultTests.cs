using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Rules;
using ArchitectureGuardian.Tests.Diagnostics;
using Xunit;

namespace ArchitectureGuardian.Tests.Analysis;

/// <summary>Verifies coverage invariants, reproducible result ordering and severity-policy isolation.</summary>
public sealed class AnalysisResultTests
{
    /// <summary>Checks that error findings cannot change the independent coverage outcome.</summary>
    /// <param name="status">The coverage status to construct.</param>
    /// <param name="isComplete">Whether that status should indicate complete coverage.</param>
    [Theory]
    [InlineData(AnalysisStatus.Completed, true)]
    [InlineData(AnalysisStatus.Incomplete, false)]
    [InlineData(AnalysisStatus.Failed, false)]
    [InlineData(AnalysisStatus.Cancelled, false)]
    public void Status_is_independent_of_violation_severity(AnalysisStatus status, bool isComplete)
    {
        AnalysisIssue[] issues = status is AnalysisStatus.Incomplete or AnalysisStatus.Failed
            ? [new("missing-context", "The dependency snapshot is unavailable.")]
            : [];
        var result = new AnalysisResult(status, [new(DiagnosticTests.Finding(), DiagnosticSeverity.Error)], issues);

        Assert.Equal(status, result.Status);
        Assert.Equal(isComplete, result.IsComplete);
        Assert.Single(result.Diagnostics);
    }

    /// <summary>Prevents incomplete or failed results from omitting the reason coverage was lost.</summary>
    /// <param name="status">The unfinished outcome that must include an operational issue.</param>
    [Theory]
    [InlineData(AnalysisStatus.Incomplete)]
    [InlineData(AnalysisStatus.Failed)]
    public void Unfinished_results_require_operational_context(AnalysisStatus status)
    {
        Assert.Throws<ArgumentException>(() => new AnalysisResult(status));
        Assert.Throws<ArgumentException>(() => new RuleEvaluationResult(status));
    }

    /// <summary>Checks that operational problems, undefined statuses and null issues cannot produce valid completion.</summary>
    [Fact]
    public void Operational_problems_cannot_be_disguised_as_completed_analysis()
    {
        AnalysisIssue[] issues = [new("load-failed", "Billing project could not be loaded.")];

        Assert.Throws<ArgumentException>(() => new AnalysisResult(AnalysisStatus.Completed, issues: issues));
        Assert.Throws<ArgumentException>(() => new RuleEvaluationResult(AnalysisStatus.Completed, issues: issues));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalysisResult((AnalysisStatus)42));
        Assert.Throws<ArgumentException>(() => new AnalysisResult(AnalysisStatus.Failed, issues: [null!]));
    }

    /// <summary>Checks that shuffled findings and issues produce identical result ordering, including severity ties.</summary>
    [Fact]
    public void All_result_collections_have_reproducible_ordering()
    {
        var violations = new[] { DiagnosticTests.Finding(), DiagnosticTests.Finding(rule: RuleCatalog.ModuleCycles) };
        AnalysisIssue[] issues = [new("missing-project", "Z"), new("missing-project", "A"), new("load-failed", "B")];
        var first = new RuleEvaluationResult(AnalysisStatus.Incomplete, violations, issues);
        var second = new RuleEvaluationResult(AnalysisStatus.Incomplete, violations.Reverse(), issues.Reverse());

        Assert.Equal(first.Violations.Select(item => item.Id), second.Violations.Select(item => item.Id));
        Assert.Equal(first.Issues, second.Issues);

        var diagnostics = new[] { new Diagnostic(violations[0], DiagnosticSeverity.Warning), new Diagnostic(violations[0], DiagnosticSeverity.Error) };
        Assert.Equal(new AnalysisResult(AnalysisStatus.Incomplete, diagnostics, issues).Diagnostics.Select(item => item.Severity),
            new AnalysisResult(AnalysisStatus.Incomplete, diagnostics.Reverse(), issues.Reverse()).Diagnostics.Select(item => item.Severity));
    }

    /// <summary>Checks defensive policy copies, case-sensitive rule matching and rejection of undefined severities.</summary>
    [Fact]
    public void Severity_policy_snapshots_overrides_and_uses_ordinal_rule_ids()
    {
        var overrides = new Dictionary<string, DiagnosticSeverity> { ["AG001"] = DiagnosticSeverity.Warning };
        var policy = new SeverityPolicy(overrides, DiagnosticSeverity.Info);
        overrides["AG001"] = DiagnosticSeverity.Error;

        Assert.Equal(DiagnosticSeverity.Warning, policy.GetSeverity("AG001"));
        Assert.Equal(DiagnosticSeverity.Info, policy.GetSeverity("AG002"));
        Assert.Equal(DiagnosticSeverity.Info, policy.GetSeverity("ag001"));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, DiagnosticSeverity>)policy.RuleSeverities).Clear());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SeverityPolicy(defaultSeverity: (DiagnosticSeverity)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SeverityPolicy(new Dictionary<string, DiagnosticSeverity> { ["AG001"] = (DiagnosticSeverity)42 }));
    }
}
