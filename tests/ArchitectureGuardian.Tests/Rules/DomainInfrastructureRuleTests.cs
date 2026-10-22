using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Repositories;
using ArchitectureGuardian.Core.Rules;
using Xunit;

namespace ArchitectureGuardian.Tests.Rules;

/// <summary>Verifies explicit AG006 restrictions, evidence, reporting and incomplete coverage independently of Roslyn.</summary>
public sealed class DomainInfrastructureRuleTests
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
        var result = await new DomainInfrastructureRule().EvaluateAsync(Context([Use(assembly, ns)]), TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisStatus.Completed, result.Status);
        Assert.Equal(count, result.Violations.Count);
        Assert.All(result.Violations, item =>
        {
            Assert.Equal("AG006", item.Rule.Id);
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
        var rules = policy.SelectRules<DependencyBuildResult>([new DomainInfrastructureRule(), new ModuleDependenciesRule()]);
        var result = await new AnalysisService<DependencyBuildResult>(rules).AnalyzeAsync(new(context, policy.SeverityPolicy), TestContext.Current.CancellationToken);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(2, diagnostic.Evidence.Count);
        Assert.False(policy.Gate.Passes(result));
        var reversed = await new DomainInfrastructureRule().EvaluateAsync(Context([second, first]), TestContext.Current.CancellationToken);
        Assert.Equal(diagnostic.Id, Assert.Single(reversed.Violations).Id);
    }

    /// <summary>Missing source ownership and missing bound reference identities cannot produce a completed clean result.</summary>
    [Fact]
    public async Task Missing_identity_and_upstream_coverage_preserve_available_findings()
    {
        var context = Context([Use("Forbidden.Library", "Allowed"),
            new("Missing.csproj", null, new("Unknown source."), targetSymbol: "Target.Api"),
            new(Project, null, new("Legacy semantic evidence."), targetSymbol: "Target.Api")]);
        var rule = new DomainInfrastructureRule();
        var result = await rule.EvaluateAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Single(result.Violations);
        Assert.Contains(result.Issues, issue => issue.Code == "dependency-project-unmapped");
        Assert.Contains(result.Issues, issue => issue.Code == "dependency-reference-identity-unavailable");
        var upstream = new AnalysisIssue("partial-input", "Input is partial.");
        foreach (var status in new[] { AnalysisStatus.Incomplete, AnalysisStatus.Failed, AnalysisStatus.Cancelled })
        {
            var partial = await rule.EvaluateAsync(new(context.Repository, context.Graph, status, [upstream]), TestContext.Current.CancellationToken);
            Assert.Equal(status, partial.Status);
            Assert.Single(partial.Violations);
            Assert.Contains(upstream, partial.Issues);
            var missing = await rule.EvaluateAsync(new(null, null, status, [upstream]), TestContext.Current.CancellationToken);
            Assert.Equal(status, missing.Status);
            Assert.Empty(missing.Violations);
        }

        await Assert.ThrowsAsync<ArgumentNullException>(() => rule.EvaluateAsync(null!, TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => rule.EvaluateAsync(context, cancellation.Token));
    }

    /// <summary>Explicit namespace exceptions include descendants and are limited to their configured layer and rule.</summary>
    /// <param name="ns">The target namespace compared against the scoped exception.</param>
    /// <param name="count">The expected AG006 restrictions remaining.</param>
    [Theory]
    [InlineData("System.Net.Abstractions", 0)]
    [InlineData("System.Net.Abstractions.Nested", 0)]
    [InlineData("System.Net.AbstractionsLike", 2)]
    [InlineData("System.Net.Other", 2)]
    public async Task Namespace_exceptions_are_explicit_and_segment_scoped(string ns, int count)
    {
        var result = await new DomainInfrastructureRule().EvaluateAsync(Context([Use("Forbidden.Library", ns)]), TestContext.Current.CancellationToken);
        Assert.Equal(count, result.Violations.Count);
    }

    /// <summary>Unconfigured layers pass, while independent AG002/AG005/AG006 policies keep their own findings.</summary>
    [Fact]
    public async Task Layer_membership_and_rule_overlap_do_not_imply_exemptions()
    {
        var facts = new[] { new DependencyEvidence(Project, "Store.csproj", new("Infrastructure use.", new("Use.cs", 3, 1)),
            targetSymbol: "Store.Api", targetAssembly: "Store", targetNamespace: "Persistence") };
        var policy = new ArchitecturePolicy([new("App", ["*.csproj"], forbiddenNamespaces: ["Persistence"])],
            [new("Business", [Project], domainInfrastructure: new(forbiddenNamespaces: ["Persistence"])),
                new("Storage", ["Store.csproj"])],
            new Dictionary<string, DiagnosticSeverity>
            {
                ["AG002"] = DiagnosticSeverity.Error,
                ["AG005"] = DiagnosticSeverity.Warning,
                ["AG006"] = DiagnosticSeverity.Error
            });
        var repository = new RepositoryModel(Project, policy,
            [new(Project, "App", new(Project, "App", [])), new("Store.csproj", "Store", new("Store.csproj", "Store", []))]);
        var graph = new DependencyGraph([], [new(new(DependencyKind.Type, "source", "App"), new(DependencyKind.Type, "target", "Store"), facts)]);
        var context = new DependencyBuildResult(repository, graph, AnalysisStatus.Completed);
        var result = await new AnalysisService<DependencyBuildResult>(policy.SelectRules<DependencyBuildResult>(
            [new LayerDependenciesRule(), new ForbiddenReferencesRule(), new DomainInfrastructureRule()]))
            .AnalyzeAsync(new(context, policy.SeverityPolicy), TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "AG002", "AG005", "AG006" }, result.Diagnostics.Select(item => item.RuleId).Order());
        var unconfigured = new ArchitecturePolicy(policy.Modules,
            [new("Business", [Project]), new("Storage", ["Store.csproj"])], policy.SeverityPolicy.RuleSeverities);
        var cleanRepository = new RepositoryModel(Project, unconfigured, repository.Projects);
        Assert.Empty((await new DomainInfrastructureRule().EvaluateAsync(new(cleanRepository, graph, AnalysisStatus.Completed), TestContext.Current.CancellationToken)).Violations);

        var excepted = new ArchitecturePolicy(policy.Modules,
            [new("Business", [Project], domainInfrastructure: new(forbiddenNamespaces: ["Persistence"], allowedNamespaces: ["Persistence"])),
                new("Storage", ["Store.csproj"])], policy.SeverityPolicy.RuleSeverities);
        var exceptedRepository = new RepositoryModel(Project, excepted, repository.Projects);
        var exceptedContext = new DependencyBuildResult(exceptedRepository, graph, AnalysisStatus.Completed);
        Assert.Empty((await new DomainInfrastructureRule().EvaluateAsync(exceptedContext, TestContext.Current.CancellationToken)).Violations);
        Assert.Single((await new ForbiddenReferencesRule().EvaluateAsync(exceptedContext, TestContext.Current.CancellationToken)).Violations);
        Assert.Single((await new LayerDependenciesRule().EvaluateAsync(exceptedContext, TestContext.Current.CancellationToken)).Violations);
    }

    private static DependencyEvidence Use(string assembly, string ns, int line = 3) => new(Project, null,
        new("Bound reference.", new("Use.cs", line, 1)), targetSymbol: "Target.Api", targetAssembly: assembly, targetNamespace: ns);

    private static DependencyBuildResult Context(IEnumerable<DependencyEvidence> facts)
    {
        var policy = new ArchitecturePolicy([new("App", [Project])],
            [new("Business", [Project], domainInfrastructure: new(["Forbidden.Library"], ["System.Net"], ["System.Net.Abstractions"]))],
            new Dictionary<string, DiagnosticSeverity> { ["AG006"] = DiagnosticSeverity.Error });
        var repository = new RepositoryModel(Project, policy, [new(Project, "App", new(Project, "App", []))]);
        var edges = facts.Select(fact => new DependencyEdge(new(DependencyKind.Assembly, "source", "App"),
            new(DependencyKind.Assembly, "target", "Target"), [fact]));
        return new(repository, new DependencyGraph([], edges), AnalysisStatus.Completed);
    }
}
