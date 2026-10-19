using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Repositories;
using ArchitectureGuardian.Core.Rules;
using Xunit;

namespace ArchitectureGuardian.Tests.Rules;

/// <summary>Verifies explicit AG005 restrictions, evidence, reporting and incomplete coverage independently of Roslyn.</summary>
public sealed class ForbiddenReferencesRuleTests
{
    private const string Project = "App.csproj";

    /// <summary>Matches exact assemblies and namespace descendants while rejecting spelling and case lookalikes.</summary>
    /// <param name="assembly">The bound assembly simple name.</param>
    /// <param name="ns">The bound namespace.</param>
    /// <param name="count">The expected independent restrictions violated.</param>
    [Theory]
    [InlineData("Forbidden.Library", "System.Net", 2)]
    [InlineData("Forbidden.Library", "System.Net.Http", 2)]
    [InlineData("Forbidden.Library.Extras", "System.NetLike", 0)]
    [InlineData("forbidden.Library", "system.Net", 0)]
    [InlineData("Allowed", "Company.System.Net", 0)]
    [InlineData("Allowed", "", 0)]
    public async Task Restrictions_use_bound_names_and_segment_boundaries(string assembly, string ns, int count)
    {
        var result = await new ForbiddenReferencesRule().EvaluateAsync(Context([Use(assembly, ns)]));
        Assert.Equal(AnalysisStatus.Completed, result.Status);
        Assert.Equal(count, result.Violations.Count);
        Assert.All(result.Violations, item =>
        {
            Assert.Equal("AG005", item.Rule.Id);
            Assert.Equal("App", item.Target.Module);
            Assert.Equal("Target.Api", item.Target.Symbol);
            Assert.Contains("restriction '", item.Message);
            Assert.Equal("Use.cs", item.Location!.Path);
            Assert.Single(item.Evidence);
        });
    }

    /// <summary>Unused assembly references pass; copied aggregate facts are retained once per symbol and restriction.</summary>
    [Fact]
    public async Task Semantic_evidence_is_deduplicated_and_reporting_applies_after_selection()
    {
        var first = Use("Forbidden.Library", "Allowed");
        var second = Use("Forbidden.Library", "Allowed", 8);
        var context = Context([first, second, first, new(Project, null, new("Unused forbidden reference."))]);
        var policy = context.Repository!.Policy;
        var rules = policy.SelectRules<DependencyBuildResult>([new ForbiddenReferencesRule(), new ModuleDependenciesRule()]);
        var result = await new AnalysisService<DependencyBuildResult>(rules).AnalyzeAsync(new(context, policy.SeverityPolicy));
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(2, diagnostic.Evidence.Count);
        Assert.False(policy.Gate.Passes(result));
        var reversed = await new ForbiddenReferencesRule().EvaluateAsync(Context([second, first]));
        Assert.Equal(diagnostic.Id, Assert.Single(reversed.Violations).Id);
    }

    /// <summary>Missing source ownership and missing bound reference identities cannot produce a completed clean result.</summary>
    [Fact]
    public async Task Missing_identity_and_upstream_coverage_preserve_available_findings()
    {
        var context = Context([Use("Forbidden.Library", "Allowed"),
            new("Missing.csproj", null, new("Unknown source."), targetSymbol: "Target.Api"),
            new(Project, null, new("Legacy semantic evidence."), targetSymbol: "Target.Api")]);
        var rule = new ForbiddenReferencesRule();
        var result = await rule.EvaluateAsync(context);
        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Single(result.Violations);
        Assert.Contains(result.Issues, issue => issue.Code == "dependency-project-unmapped");
        Assert.Contains(result.Issues, issue => issue.Code == "dependency-reference-identity-unavailable");
        var upstream = new AnalysisIssue("partial-input", "Input is partial.");
        foreach (var status in new[] { AnalysisStatus.Incomplete, AnalysisStatus.Failed, AnalysisStatus.Cancelled })
        {
            var partial = await rule.EvaluateAsync(new(context.Repository, context.Graph, status, [upstream]));
            Assert.Equal(status, partial.Status);
            Assert.Single(partial.Violations);
            Assert.Contains(upstream, partial.Issues);
            var missing = await rule.EvaluateAsync(new(null, null, status, [upstream]));
            Assert.Equal(status, missing.Status);
            Assert.Empty(missing.Violations);
        }

        await Assert.ThrowsAsync<ArgumentNullException>(() => rule.EvaluateAsync(null!));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => rule.EvaluateAsync(context, cancellation.Token));
    }

    private static DependencyEvidence Use(string assembly, string ns, int line = 3) => new(Project, null,
        new("Bound reference.", new("Use.cs", line, 1)), targetSymbol: "Target.Api", targetAssembly: assembly, targetNamespace: ns);

    private static DependencyBuildResult Context(IEnumerable<DependencyEvidence> facts)
    {
        var policy = new ArchitecturePolicy([new("App", [Project], forbiddenAssemblies: ["Forbidden.Library"])],
            [new("All", [Project], forbiddenNamespaces: ["System.Net"])],
            new Dictionary<string, DiagnosticSeverity> { ["AG005"] = DiagnosticSeverity.Error });
        var repository = new RepositoryModel(Project, policy, [new(Project, "App", new(Project, "App", []))]);
        var edges = facts.Select(fact => new DependencyEdge(new(DependencyKind.Assembly, "source", "App"),
            new(DependencyKind.Assembly, "target", "Target"), [fact]));
        return new(repository, new DependencyGraph([], edges), AnalysisStatus.Completed);
    }
}
