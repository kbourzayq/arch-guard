using System.Globalization;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;
using Xunit;

namespace ArchitectureGuardian.Tests.Diagnostics;

/// <summary>Verifies the canonical rule catalog and immutable, reproducible diagnostic contracts.</summary>
public sealed class DiagnosticTests
{
    /// <summary>Locks core rule IDs to their intended meanings and checks that catalog metadata is read-only.</summary>
    [Fact]
    public void Catalog_preserves_canonical_rule_identities()
    {
        Assert.Equal(["AG001", "AG002", "AG003", "AG004", "AG005", "AG006"], RuleCatalog.All.Select(rule => rule.Id));
        Assert.Equal(["Module dependencies", "Layer dependencies", "Module cycles", "Implementation boundaries",
            "Forbidden assembly/namespace references", "Domain infrastructure references"], RuleCatalog.All.Select(rule => rule.Title));
        Assert.All(RuleCatalog.All, rule => Assert.Equal("https", rule.DocumentationUrl.Scheme));
        Assert.Throws<NotSupportedException>(() => ((IList<RuleDescriptor>)RuleCatalog.All).Clear());
    }

    /// <summary>Checks that a reported diagnostic retains all evidence and guidance, with or without a source span.</summary>
    [Fact]
    public void Diagnostic_exposes_evidence_and_guidance_with_optional_location()
    {
        var location = new SourceLocation("src/Orders/Order.cs", 10, 5, 10, 20);
        var violation = Finding(location: location);
        var diagnostic = new Diagnostic(violation, DiagnosticSeverity.Warning);

        Assert.Equal("AG001", diagnostic.RuleId);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Orders depends on Billing internals.", diagnostic.Message);
        Assert.Equal(location, diagnostic.Location);
        Assert.Equal("Orders", diagnostic.Target.Module);
        Assert.Equal("Orders.Order", diagnostic.Target.Symbol);
        Assert.Equal("Orders -> Billing", Assert.Single(diagnostic.Evidence).Description);
        Assert.Equal("Depend on Billing contracts.", diagnostic.Remediation);
        Assert.Equal(RuleCatalog.ModuleDependencies.DocumentationUrl, diagnostic.DocumentationUrl);
        Assert.Null(Finding().Location);
    }

    /// <summary>Exercises culture and path variations that must not change canonical identities or ordering.</summary>
    [Fact]
    public void Identity_and_order_ignore_input_enumeration_culture_and_path_separators()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var first = Finding(evidence: [new("I edge"), new("ı edge")],
                location: new SourceLocation("src\\Orders\\Order.cs", 1234, 5));
            var other = Finding(target: new DiagnosticTarget("Billing"));
            var firstResult = new AnalysisResult(AnalysisStatus.Completed,
                [new(other, DiagnosticSeverity.Error), new(first, DiagnosticSeverity.Warning)]);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            var repeated = Finding(evidence: [new("ı edge"), new("I edge")],
                location: new SourceLocation("src/Orders/Order.cs", 1234, 5));
            var secondResult = new AnalysisResult(AnalysisStatus.Completed,
                [new(repeated, DiagnosticSeverity.Warning), new(other, DiagnosticSeverity.Error)]);

            Assert.Equal(first.Id, repeated.Id);
            Assert.Matches("^[0-9a-f]{64}$", first.Id);
            Assert.Equal(first.Evidence, repeated.Evidence);
            Assert.Equal(firstResult.Diagnostics.Select(item => item.Id), secondResult.Diagnostics.Select(item => item.Id));
            Assert.Equal(new Diagnostic(first, DiagnosticSeverity.Info).Id, new Diagnostic(first, DiagnosticSeverity.Error).Id);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    /// <summary>Checks that semantically different rule occurrences do not collapse to the same snapshot identity.</summary>
    [Fact]
    public void Different_rule_target_location_and_evidence_have_distinct_identities()
    {
        var findings = new[]
        {
            Finding(),
            Finding(rule: RuleCatalog.LayerDependencies),
            Finding(target: new DiagnosticTarget("Billing")),
            Finding(location: new SourceLocation("src/Order.cs", 1, 1)),
            Finding(location: new SourceLocation("src/Order.cs", 2, 1)),
            Finding(evidence: [new("Orders -> Shipping")]),
        };

        Assert.Equal(findings.Length, findings.Select(finding => finding.Id).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Locks the version-one identity format to a digest reproducible across processes and platforms.</summary>
    [Fact]
    public void Snapshot_identity_v1_has_a_fixed_cross_process_reference_value()
    {
        Assert.Equal("eba0bc641462dae33e41cd5a7ee04bca778c157d477bbf3e61f311fdc6a405da", Finding().Id);
    }

    /// <summary>Checks that mutating caller collections or casting exposed lists cannot corrupt captured findings.</summary>
    [Fact]
    public void Collections_are_defensive_snapshots_and_reject_mutation()
    {
        var evidence = new List<DiagnosticEvidence> { new("Orders -> Billing") };
        var violation = Finding(evidence: evidence);
        var violations = new List<RuleViolation> { violation };
        var evaluation = new RuleEvaluationResult(AnalysisStatus.Completed, violations);
        var diagnostics = new List<Diagnostic> { new(violation, DiagnosticSeverity.Error) };
        var issues = new List<AnalysisIssue> { new("missing-project", "Billing was not loaded.") };
        var result = new AnalysisResult(AnalysisStatus.Incomplete, diagnostics, issues);

        evidence.Clear();
        violations.Clear();
        diagnostics.Clear();
        issues.Clear();

        Assert.Single(violation.Evidence);
        Assert.Single(evaluation.Violations);
        Assert.Single(result.Diagnostics);
        Assert.Single(result.Issues);
        Assert.Throws<NotSupportedException>(() => ((IList<DiagnosticEvidence>)violation.Evidence).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<RuleViolation>)evaluation.Violations).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Diagnostic>)result.Diagnostics).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<AnalysisIssue>)result.Issues).Clear());
    }

    /// <summary>Checks rejection of non-positive coordinates, reversed ranges and partially supplied end positions.</summary>
    /// <param name="line">The candidate start line.</param>
    /// <param name="column">The candidate start column.</param>
    /// <param name="endLine">The candidate exclusive end line, if supplied.</param>
    /// <param name="endColumn">The candidate exclusive end column, if supplied.</param>
    [Theory]
    [InlineData(0, 1, null, null)]
    [InlineData(1, 0, null, null)]
    [InlineData(2, 3, 1, 4)]
    [InlineData(2, 3, 2, 2)]
    [InlineData(2, 3, 3, 0)]
    [InlineData(1, 1, 2, null)]
    [InlineData(1, 1, null, 2)]
    public void Invalid_source_coordinates_are_rejected(int line, int column, int? endLine, int? endColumn)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SourceLocation("src/Order.cs", line, column, endLine, endColumn));
    }

    /// <summary>Checks that targets, evidence, documentation links and severity values satisfy their contract invariants.</summary>
    [Fact]
    public void Invalid_diagnostic_inputs_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new DiagnosticTarget());
        Assert.Throws<ArgumentException>(() => new DiagnosticTarget(" "));
        Assert.Throws<ArgumentException>(() => new DiagnosticTarget(symbol: ""));
        Assert.Throws<ArgumentException>(() => Finding(evidence: []));
        Assert.Throws<ArgumentException>(() => Finding(evidence: [null!]));
        Assert.Throws<ArgumentException>(() => new DiagnosticEvidence(" "));
        Assert.Throws<ArgumentException>(() => new RuleDescriptor("AG001", "Modules", new Uri("relative", UriKind.Relative)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Diagnostic(Finding(), (DiagnosticSeverity)42));
    }

    /// <summary>Creates a representative module violation while allowing a test to vary identity-bearing fields.</summary>
    /// <param name="rule">The rule descriptor, or null for AG001.</param>
    /// <param name="target">The affected identifiers, or null for the sample Orders symbol.</param>
    /// <param name="evidence">Supporting facts, or null for the sample dependency.</param>
    /// <param name="location">An optional primary source span.</param>
    /// <returns>A finding with consistent message and remediation text for comparisons.</returns>
    internal static RuleViolation Finding(RuleDescriptor? rule = null, DiagnosticTarget? target = null,
        IEnumerable<DiagnosticEvidence>? evidence = null, SourceLocation? location = null) =>
        new(rule ?? RuleCatalog.ModuleDependencies, "Orders depends on Billing internals.",
            target ?? new DiagnosticTarget("Orders", "Orders.Order"), evidence ?? [new("Orders -> Billing")],
            "Depend on Billing contracts.", location);
}
