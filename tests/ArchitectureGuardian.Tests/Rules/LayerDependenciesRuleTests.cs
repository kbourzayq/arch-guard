using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Repositories;
using ArchitectureGuardian.Core.Rules;
using Xunit;

namespace ArchitectureGuardian.Tests.Rules;

/// <summary>Verifies policy-defined AG002 layer permissions, evidence, selection and coverage.</summary>
public sealed class LayerDependenciesRuleTests
{
    private const string Domain = "Billing/Domain.csproj";
    private const string Infrastructure = "Billing/Infrastructure.csproj";
    private const string Contracts = "Billing/Contracts.csproj";
    private const string Presentation = "Orders/Presentation.csproj";
    private const string OtherDomain = "Orders/Domain.csproj";

    /// <summary>Layer permissions are direct and directional, independent of module and contract permissions.</summary>
    /// <param name="source">The originating project.</param>
    /// <param name="target">The referenced project, or external metadata.</param>
    /// <param name="allowed">Whether the configured layer matrix permits the dependency.</param>
    [Theory]
    [InlineData(Domain, Infrastructure, false)]
    [InlineData(Domain, Contracts, true)]
    [InlineData(Infrastructure, Domain, true)]
    [InlineData(Contracts, Domain, false)]
    [InlineData(Presentation, Infrastructure, true)]
    [InlineData(Presentation, Domain, false)]
    [InlineData(OtherDomain, Infrastructure, false)]
    [InlineData(Domain, OtherDomain, true)]
    [InlineData(Domain, Domain, true)]
    [InlineData(Domain, null, true)]
    public async Task Configured_layer_matrix_is_enforced(string source, string? target, bool allowed)
    {
        var context = Context([Evidence(source, target)]);
        var result = await AnalyzeAsync(context);

        Assert.Equal(AnalysisStatus.Completed, result.Status);
        Assert.Equal(allowed ? 0 : 1, result.Diagnostics.Count);
        Assert.Empty(result.Issues);
        Assert.Equal(allowed, context.Repository!.Policy.Gate.Passes(result));
    }

    /// <summary>Names and permissions come entirely from policy; no fixed Clean Architecture layout is assumed.</summary>
    [Fact]
    public async Task Arbitrary_layer_names_and_explicit_reverse_permissions_are_supported()
    {
        var context = Context([Evidence(Domain, Infrastructure)]);
        var policy = context.Repository!.Policy;
        var custom = new ArchitecturePolicy(policy.Modules, [
            new("Kernel", ["**/Domain.csproj"], ["Adapters"]),
            new("Adapters", [Infrastructure]),
            new("Api", [Contracts]),
            new("Entry", [Presentation]),
        ], policy.SeverityPolicy.RuleSeverities);
        var repository = new RepositoryModel("App.slnx", custom, context.Repository.Projects);
        var result = await AnalyzeAsync(new(repository, context.Graph, AnalysisStatus.Completed));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(AnalysisStatus.Completed, result.Status);

        var reverse = await AnalyzeAsync(new(repository,
            new DependencyGraph([], [Edge(DependencyKind.Project, [Evidence(Infrastructure, Domain)])]), AnalysisStatus.Completed));
        var finding = Assert.Single(reverse.Diagnostics);
        Assert.Contains("Layer 'Adapters'", finding.Message);
        Assert.Contains("layer 'Kernel'", finding.Message);
    }

    /// <summary>Evaluates origin project pairs even when an aggregate combines allowed and forbidden targets.</summary>
    /// <param name="kind">The graph level carrying dependency evidence.</param>
    [Theory]
    [InlineData(DependencyKind.Project)]
    [InlineData(DependencyKind.Assembly)]
    [InlineData(DependencyKind.Namespace)]
    [InlineData(DependencyKind.Type)]
    [InlineData(DependencyKind.Module)]
    [InlineData(DependencyKind.Layer)]
    public async Task Original_project_pairs_determine_layer_permissions_at_every_graph_level(DependencyKind kind)
    {
        var result = await AnalyzeAsync(Context([Evidence(Domain, Infrastructure), Evidence(Domain, Contracts)], kind: kind));
        var finding = Assert.Single(result.Diagnostics);

        Assert.Equal("AG002", finding.RuleId);
        Assert.Equal("Billing", finding.Target.Module);
        Assert.Contains("Layer 'Domain'", finding.Message);
        Assert.Contains("layer 'Infrastructure'", finding.Message);
        Assert.Contains(Domain, finding.Message);
        Assert.Contains(Infrastructure, finding.Message);
        Assert.DoesNotContain(Contracts, finding.Message);
        Assert.Single(finding.Evidence);
        Assert.Null(finding.Location);
        Assert.Contains("allowedDependencies", finding.Remediation);
    }

    /// <summary>Preserves symbols and source ranges once per fact, with stable findings under input reordering.</summary>
    [Fact]
    public async Task Findings_retain_symbols_and_source_evidence_without_aggregate_duplicates()
    {
        var location = new SourceLocation("Billing/Invoice.cs", 5, 9, 5, 31);
        var reference = Evidence(Domain, Infrastructure);
        var semantic = new DependencyEvidence(Domain, Infrastructure,
            new("'Billing.Invoice.Save' uses 'Billing.Store'.", location), "Billing.Invoice.Save", "Billing.Store");
        var context = Context([reference, semantic, Evidence(Domain, Contracts), Evidence(Presentation, Domain)]);
        var graph = new DependencyGraph(context.Graph!.Nodes, context.Graph.Edges.Concat([
            Edge(DependencyKind.Type, [semantic]), Edge(DependencyKind.Namespace, [semantic]),
        ])).Aggregate(context.Repository!);
        var first = await AnalyzeAsync(new(context.Repository, graph, AnalysisStatus.Completed));
        var reversed = new DependencyGraph(graph.Nodes.Reverse(), graph.Edges.Reverse().Select(edge =>
            new DependencyEdge(edge.Source, edge.Target, edge.Evidence.Reverse())));
        var second = await AnalyzeAsync(new(context.Repository, reversed, AnalysisStatus.Completed));

        Assert.Equal(first.Diagnostics.Select(item => item.Id), second.Diagnostics.Select(item => item.Id));
        Assert.Equal(2, first.Diagnostics.Count);
        var finding = Assert.Single(first.Diagnostics, item => item.Message.Contains(Infrastructure, StringComparison.Ordinal));
        Assert.Equal(location, finding.Location);
        Assert.Equal(2, finding.Evidence.Count);
        Assert.Contains(reference.Fact, finding.Evidence);
        Assert.Contains(semantic.Fact, finding.Evidence);
        Assert.Contains(finding.Evidence, fact => fact.Description.Contains("Billing.Invoice.Save", StringComparison.Ordinal)
            && fact.Description.Contains("Billing.Store", StringComparison.Ordinal));
    }

    /// <summary>Severity and gate configuration apply after discovery without changing finding identity.</summary>
    /// <param name="severity">The configured reporting level.</param>
    [Theory]
    [InlineData(DiagnosticSeverity.Info)]
    [InlineData(DiagnosticSeverity.Warning)]
    [InlineData(DiagnosticSeverity.Error)]
    public async Task Severity_is_separate_from_layer_evaluation(DiagnosticSeverity severity)
    {
        var context = Context([Evidence(Domain, Infrastructure)], severity: severity);
        var result = await AnalyzeAsync(context);
        var diagnostic = Assert.Single(result.Diagnostics);
        var raw = Assert.Single((await new LayerDependenciesRule().EvaluateAsync(context, TestContext.Current.CancellationToken)).Violations);

        Assert.Equal(severity, diagnostic.Severity);
        Assert.Equal(raw.Id, diagnostic.Id);
        Assert.Equal(severity < DiagnosticSeverity.Error, context.Repository!.Policy.Gate.Passes(result));
    }

    /// <summary>Projects outside AG002's selected scope pass module evaluation without an implicit layer check.</summary>
    [Fact]
    public async Task Omitting_AG002_does_not_evaluate_layers()
    {
        var context = Context([Evidence(Domain, Infrastructure)], selectedRule: "AG001");
        var policy = context.Repository!.Policy;
        var selected = policy.SelectRules<DependencyBuildResult>([new ModuleDependenciesRule(), new LayerDependenciesRule()]);
        var result = await new AnalysisService<DependencyBuildResult>(selected).AnalyzeAsync(new(context, policy.SeverityPolicy), TestContext.Current.CancellationToken);

        Assert.Equal("AG001", Assert.Single(selected).Descriptor.Id);
        Assert.Equal(AnalysisStatus.Completed, result.Status);
        Assert.Empty(result.Diagnostics);
    }

    /// <summary>Composing both rules enforces module and layer constraints independently on the same snapshot.</summary>
    [Fact]
    public async Task Module_and_layer_rules_enforce_independent_dimensions_together()
    {
        var context = Context([Evidence(Domain, Infrastructure), Evidence(Domain, OtherDomain)]);
        var policy = context.Repository!.Policy;
        var selection = new ArchitecturePolicy(policy.Modules, policy.Layers,
            new Dictionary<string, DiagnosticSeverity> { ["AG001"] = DiagnosticSeverity.Warning, ["AG002"] = DiagnosticSeverity.Error });
        var rules = selection.SelectRules<DependencyBuildResult>([new LayerDependenciesRule(), new ModuleDependenciesRule()]);
        var result = await new AnalysisService<DependencyBuildResult>(rules).AnalyzeAsync(new(context, selection.SeverityPolicy), TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisStatus.Completed, result.Status);
        Assert.Equal(2, result.Diagnostics.Count);
        var module = Assert.Single(result.Diagnostics, item => item.RuleId == "AG001");
        Assert.Contains(OtherDomain, module.Message);
        Assert.Equal(DiagnosticSeverity.Warning, module.Severity);
        var layer = Assert.Single(result.Diagnostics, item => item.RuleId == "AG002");
        Assert.Contains(Infrastructure, layer.Message);
        Assert.Equal(DiagnosticSeverity.Error, layer.Severity);
    }

    /// <summary>Unknown project evidence cannot silently pass as outside scope; known violations remain visible.</summary>
    /// <param name="source">A mapped or unmapped source project.</param>
    /// <param name="target">A mapped or unmapped target project.</param>
    [Theory]
    [InlineData("Missing.csproj", Infrastructure)]
    [InlineData(Domain, "Missing.csproj")]
    public async Task Unmapped_evidence_prevents_completed_coverage(string source, string target)
    {
        var result = await AnalyzeAsync(Context([Evidence(source, target), Evidence(Domain, Infrastructure)]));

        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Single(result.Diagnostics);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("dependency-project-unmapped", issue.Code);
        Assert.Equal("AG002", issue.RuleId);
    }

    /// <summary>Preserves upstream outcomes and available findings even when extraction was unfinished.</summary>
    /// <param name="status">The upstream coverage outcome.</param>
    [Theory]
    [InlineData(AnalysisStatus.Incomplete)]
    [InlineData(AnalysisStatus.Failed)]
    [InlineData(AnalysisStatus.Cancelled)]
    public async Task Partial_coverage_keeps_findings_and_issues(AnalysisStatus status)
    {
        var complete = Context([Evidence(Domain, Infrastructure)]);
        var issue = new AnalysisIssue("extraction-coverage", "Dependency extraction has partial coverage.");
        var result = await AnalyzeAsync(new(complete.Repository, complete.Graph, status, [issue]));

        Assert.Equal(status, result.Status);
        Assert.Single(result.Diagnostics);
        Assert.Equal(issue, Assert.Single(result.Issues));
        Assert.False(complete.Repository!.Policy.Gate.Passes(result));
    }

    /// <summary>Missing context stays incomplete instead of becoming a clean layer evaluation.</summary>
    /// <param name="missingRepository">Whether the repository rather than the graph is absent.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_context_keeps_coverage_issues(bool missingRepository)
    {
        var complete = Context([]);
        var issue = new AnalysisIssue("missing-context", "Dependency context is unavailable.");
        var result = await new LayerDependenciesRule().EvaluateAsync(new(missingRepository ? null : complete.Repository,
            missingRepository ? complete.Graph : null, AnalysisStatus.Incomplete, [issue]), TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Empty(result.Violations);
        Assert.Equal(issue, Assert.Single(result.Issues));
    }

    /// <summary>Rejects absent input and cooperates with rule and service cancellation.</summary>
    [Fact]
    public async Task Null_context_and_requested_cancellation_are_observed()
    {
        var rule = new LayerDependenciesRule();
        await Assert.ThrowsAsync<ArgumentNullException>(() => rule.EvaluateAsync(null!, TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => rule.EvaluateAsync(Context([]), cancellation.Token));
        var result = await new AnalysisService<DependencyBuildResult>([rule]).AnalyzeAsync(new(Context([])), cancellation.Token);
        Assert.Equal(AnalysisStatus.Cancelled, result.Status);
    }

    private static Task<AnalysisResult> AnalyzeAsync(DependencyBuildResult context)
    {
        var policy = context.Repository!.Policy;
        var selected = policy.SelectRules<DependencyBuildResult>([new LayerDependenciesRule()]);
        return new AnalysisService<DependencyBuildResult>(selected).AnalyzeAsync(new(context, policy.SeverityPolicy));
    }

    private static DependencyBuildResult Context(IEnumerable<DependencyEvidence> evidence,
        DependencyKind kind = DependencyKind.Project, DiagnosticSeverity severity = DiagnosticSeverity.Error, string selectedRule = "AG002")
    {
        var policy = new ArchitecturePolicy([
            new("Billing", ["Billing/*.csproj"], [new("api", [Infrastructure])]),
            new("Orders", ["Orders/*.csproj"], allowedDependencies: ["contract:Billing/api"]),
        ], [
            new("Domain", ["**/Domain.csproj"], ["Contracts"]),
            new("Infrastructure", [Infrastructure], ["Domain"]),
            new("Contracts", [Contracts]),
            new("Presentation", [Presentation], ["Infrastructure"]),
        ], new Dictionary<string, DiagnosticSeverity> { [selectedRule] = severity });
        var repository = new RepositoryModel("App.slnx", policy,
            new[] { Domain, Infrastructure, Contracts, Presentation, OtherDomain }
                .Select(path => new RepositoryProject(path, path, new(path, path, []))));
        var edges = evidence.GroupBy(item => (item.SourceProject, item.TargetProject)).Select(group => Edge(kind, group));
        return new(repository, new DependencyGraph([], edges), AnalysisStatus.Completed);
    }

    private static DependencyEdge Edge(DependencyKind kind, IEnumerable<DependencyEvidence> evidence)
    {
        var facts = evidence.ToArray();
        if (kind is DependencyKind.Module or DependencyKind.Layer)
        {
            return new(new(kind, "source-group", "source-group"), new(kind, "target-group", "target-group"), facts);
        }

        return new(new(kind, facts[0].SourceProject, facts[0].SourceProject, facts[0].SourceProject),
            new(kind, facts[0].TargetProject ?? "external", facts[0].TargetProject ?? "external", facts[0].TargetProject), facts);
    }

    private static DependencyEvidence Evidence(string source, string? target) =>
        new(source, target, new($"Project '{source}' references '{target ?? "external"}'."), projectReference: target);
}
