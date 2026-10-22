using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Repositories;
using ArchitectureGuardian.Core.Rules;
using Xunit;

namespace ArchitectureGuardian.Tests.Rules;

/// <summary>Verifies semantic implementation boundaries independently of module permissions and reporting policy.</summary>
public sealed class ImplementationBoundariesRuleTests
{
    private const string Orders = "Orders/App.csproj";
    private const string Contracts = "Catalog/Contracts.csproj";
    private const string Implementation = "Catalog/Store.csproj";

    /// <summary>Contract uses, internal module interactions, external metadata and unused references are allowed.</summary>
    [Fact]
    public async Task Contracts_same_module_and_nonsemantic_references_pass()
    {
        var result = await new ImplementationBoundariesRule().EvaluateAsync(Context([
            Use(Orders, Contracts), Use(Contracts, Implementation), Use(Implementation, Implementation),
            Use(Orders, null), new(Orders, Implementation, new("Unused project reference."), projectReference: Implementation),
            new(Orders, Implementation, new("Unused assembly reference.")),
        ]), TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisStatus.Completed, result.Status);
        Assert.Empty(result.Violations);
        Assert.Empty(result.Issues);
    }

    /// <summary>Whole-module permissions do not waive semantic boundaries; aggregate copies preserve exact evidence once.</summary>
    [Fact]
    public async Task Public_implementation_use_retains_symbol_spans_and_deterministic_identity()
    {
        var firstUse = Use(Orders, Implementation);
        var secondUse = Use(Orders, Implementation, line: 8);
        var otherSymbol = Use(Orders, Implementation, "Catalog.CatalogDbContext.Save()", 9);
        var context = Context([firstUse, secondUse, otherSymbol, Use(Orders, Contracts)]);
        Assert.True(context.Repository!.ResolvedPolicy.AllowsModuleDependency(Orders, Implementation));
        var duplicated = new DependencyGraph(context.Graph!.Nodes, context.Graph.Edges.Concat(context.Graph.Edges))
            .Aggregate(context.Repository);
        var first = await new ImplementationBoundariesRule().EvaluateAsync(new(context.Repository, duplicated, AnalysisStatus.Completed), TestContext.Current.CancellationToken);
        var second = await new ImplementationBoundariesRule().EvaluateAsync(Context([otherSymbol, secondUse, firstUse]), TestContext.Current.CancellationToken);

        Assert.Equal(2, first.Violations.Count);
        Assert.Equal(first.Violations.Select(item => item.Id), second.Violations.Select(item => item.Id));
        var finding = Assert.Single(first.Violations, item => item.Target.Symbol == "Catalog.CatalogDbContext");
        Assert.Equal("AG004", finding.Rule.Id);
        Assert.Equal(RuleCatalog.ImplementationBoundaries.DocumentationUrl, finding.Rule.DocumentationUrl);
        Assert.Equal("Orders", finding.Target.Module);
        Assert.Equal(2, finding.Evidence.Count);
        Assert.Contains(firstUse.Fact, finding.Evidence);
        Assert.Contains(secondUse.Fact, finding.Evidence);
        Assert.Equal(firstUse.Fact.Location, finding.Location);
        Assert.Contains("public contract", finding.Remediation);
    }

    /// <summary>Selection, severity and gates apply after boundary discovery.</summary>
    [Fact]
    public async Task Policy_selection_and_reporting_preserve_discovery()
    {
        var context = Context([Use(Orders, Implementation)]);
        var policy = context.Repository!.Policy;
        var rules = policy.SelectRules<DependencyBuildResult>([new ModuleDependenciesRule(), new ImplementationBoundariesRule()]);
        Assert.IsType<ImplementationBoundariesRule>(Assert.Single(rules));
        var result = await new AnalysisService<DependencyBuildResult>(rules).AnalyzeAsync(new(context, policy.SeverityPolicy), TestContext.Current.CancellationToken);
        Assert.Equal(DiagnosticSeverity.Error, Assert.Single(result.Diagnostics).Severity);
        Assert.False(policy.Gate.Passes(result));
    }

    /// <summary>Missing ownership prevents completion while available findings survive.</summary>
    /// <param name="source">The source of an unmapped use.</param>
    /// <param name="target">The target of an unmapped use.</param>
    [Theory]
    [InlineData("Missing.csproj", Implementation)]
    [InlineData(Orders, "Missing.csproj")]
    public async Task Unknown_ownership_preserves_findings_and_reports_incomplete(string source, string target)
    {
        var result = await new ImplementationBoundariesRule().EvaluateAsync(Context([Use(source, target), Use(Orders, Implementation)]), TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Single(result.Violations);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("dependency-project-unmapped", issue.Code);
        Assert.Equal("AG004", issue.RuleId);
    }

    /// <summary>Incomplete extraction never becomes a clean completed result.</summary>
    /// <param name="status">The upstream coverage outcome.</param>
    [Theory]
    [InlineData(AnalysisStatus.Incomplete)]
    [InlineData(AnalysisStatus.Failed)]
    [InlineData(AnalysisStatus.Cancelled)]
    public async Task Upstream_coverage_and_issues_survive(AnalysisStatus status)
    {
        var context = Context([Use(Orders, Implementation)]);
        var issue = new AnalysisIssue("partial-input", "Extraction did not complete.");
        var result = await new ImplementationBoundariesRule().EvaluateAsync(new(context.Repository, context.Graph, status, [issue]), TestContext.Current.CancellationToken);
        Assert.Equal(status, result.Status);
        Assert.Equal(issue, Assert.Single(result.Issues));
        Assert.Single(result.Violations);
        var missing = await new ImplementationBoundariesRule().EvaluateAsync(new(null, null, status, [issue]), TestContext.Current.CancellationToken);
        Assert.Equal(status, missing.Status);
        Assert.Empty(missing.Violations);
    }

    /// <summary>Rejects absent input and observes cancellation before traversing evidence.</summary>
    [Fact]
    public async Task Cancellation_and_null_input_are_preserved()
    {
        var rule = new ImplementationBoundariesRule();
        await Assert.ThrowsAsync<ArgumentNullException>(() => rule.EvaluateAsync(null!, TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => rule.EvaluateAsync(Context([]), cancellation.Token));
    }

    private static DependencyEvidence Use(string source, string? target, string symbol = "Catalog.CatalogDbContext", int line = 3) =>
        new(source, target, new($"'Order.Submit' uses '{symbol}'.", new("Orders/Use.cs", line, 5, line, 20)), "Order.Submit", symbol);

    private static DependencyBuildResult Context(IEnumerable<DependencyEvidence> facts)
    {
        var policy = new ArchitecturePolicy([
            new("Orders", [Orders], allowedDependencies: ["module:Catalog"]),
            new("Catalog", ["Catalog/*.csproj"], [new("api", [Contracts])]),
        ], [new("All", ["**/*.csproj"])], new Dictionary<string, DiagnosticSeverity> { ["AG004"] = DiagnosticSeverity.Error });
        var repository = new RepositoryModel("App.slnx", policy, new[] { Orders, Contracts, Implementation }
            .Select(path => new RepositoryProject(path, path, new(path, path, []))));
        var edges = facts.Select(fact => new DependencyEdge(
            new(DependencyKind.Type, fact.SourceProject, fact.SourceProject, fact.SourceProject),
            new(DependencyKind.Type, fact.TargetProject ?? "external", fact.TargetProject ?? "external", fact.TargetProject), [fact]));
        return new(repository, new DependencyGraph([], edges), AnalysisStatus.Completed);
    }
}
