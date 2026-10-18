using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Repositories;
using ArchitectureGuardian.Core.Rules;
using Xunit;

namespace ArchitectureGuardian.Tests.Rules;

/// <summary>Verifies AG001 permissions, evidence, policy composition and honest coverage over neutral graphs.</summary>
public sealed class ModuleDependenciesRuleTests
{
    private const string Orders = "Orders/App.csproj";
    private const string Contracts = "Billing/Contracts.csproj";
    private const string Internal = "Billing/Internal.csproj";
    private const string OtherContract = "Billing/Events.csproj";
    private const string Shipping = "Shipping/App.csproj";

    /// <summary>Contract permissions authorize exact contract projects while same-module and external uses remain allowed.</summary>
    [Fact]
    public async Task Exact_contract_same_module_and_external_dependencies_pass()
    {
        var context = Context([
            Evidence(Orders, Contracts), Evidence(Contracts, Internal), Evidence(Internal, Contracts),
            Evidence(Orders, null), Evidence(Orders, Orders),
        ]);
        var result = await AnalyzeAsync(context);

        Assert.Equal(AnalysisStatus.Completed, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Issues);
        Assert.True(context.Repository!.Policy.Gate.Passes(result));
    }

    /// <summary>Permissions are explicit, exact, directional and direct rather than inferred from contract names or paths.</summary>
    /// <param name="source">The originating project.</param>
    /// <param name="target">The referenced project.</param>
    /// <param name="permission">The optional Orders permission.</param>
    /// <param name="allowed">Whether this direct dependency is permitted.</param>
    [Theory]
    [InlineData(Orders, Contracts, null, false)]
    [InlineData(Orders, OtherContract, "contract:Billing/api", false)]
    [InlineData(Orders, Internal, "contract:Billing/api", false)]
    [InlineData(Orders, Internal, "module:Billing", true)]
    [InlineData(Contracts, Orders, "module:Billing", false)]
    [InlineData(Orders, Shipping, "module:Billing", false)]
    public async Task Permission_boundaries_are_enforced(string source, string target, string? permission, bool allowed)
    {
        var context = Context([Evidence(source, target)], permission);
        var result = await AnalyzeAsync(context);

        Assert.Equal(AnalysisStatus.Completed, result.Status);
        Assert.Equal(allowed ? 0 : 1, result.Diagnostics.Count);
        Assert.Equal(allowed, context.Repository!.Policy.Gate.Passes(result));
    }

    /// <summary>Checks origin pairs at every graph level, including semantic-only and already aggregated contexts.</summary>
    /// <param name="kind">The graph level carrying the observed dependency.</param>
    [Theory]
    [InlineData(DependencyKind.Project)]
    [InlineData(DependencyKind.Assembly)]
    [InlineData(DependencyKind.Namespace)]
    [InlineData(DependencyKind.Type)]
    [InlineData(DependencyKind.Module)]
    [InlineData(DependencyKind.Layer)]
    public async Task All_graph_levels_use_original_project_permissions(DependencyKind kind)
    {
        var result = await AnalyzeAsync(Context([Evidence(Orders, Internal), Evidence(Orders, Contracts)], kind: kind));

        var finding = Assert.Single(result.Diagnostics).Violation;
        Assert.Equal("AG001", finding.Rule.Id);
        Assert.Equal("Orders", finding.Target.Module);
        Assert.Contains("module 'Billing'", finding.Message);
        Assert.Contains(Internal, finding.Message);
        Assert.DoesNotContain(Contracts, finding.Message);
        Assert.Single(finding.Evidence);
        Assert.Null(finding.Location);
        Assert.NotEmpty(finding.Remediation);
    }

    /// <summary>Merges repeated graph-level facts without mixing allowed contracts into forbidden implementation findings.</summary>
    [Fact]
    public async Task Findings_retain_actual_evidence_once_and_are_deterministic()
    {
        var location = new SourceLocation("Orders/Submit.cs", 5, 9, 5, 31);
        var reference = Evidence(Orders, Internal);
        var semantic = new DependencyEvidence(Orders, Internal,
            new("'Orders.Submit.Create' uses 'Billing.Store'.", location), "Orders.Submit.Create", "Billing.Store");
        var context = Context([reference, semantic, Evidence(Orders, Contracts), Evidence(Orders, OtherContract)]);
        var graph = new DependencyGraph(context.Graph!.Nodes, context.Graph.Edges.Concat([
            Edge(DependencyKind.Type, [semantic]), Edge(DependencyKind.Namespace, [semantic]),
        ])).Aggregate(context.Repository!);
        var first = await AnalyzeAsync(new(context.Repository, graph, AnalysisStatus.Completed));
        var reversed = new DependencyGraph(graph.Nodes.Reverse(), graph.Edges.Reverse());
        var second = await AnalyzeAsync(new(context.Repository, reversed, AnalysisStatus.Completed));

        Assert.Equal(first.Diagnostics.Select(item => item.Id), second.Diagnostics.Select(item => item.Id));
        Assert.Equal(2, first.Diagnostics.Count);
        var finding = Assert.Single(first.Diagnostics, item => item.Violation.Message.Contains(Internal, StringComparison.Ordinal)).Violation;
        Assert.Equal(location, finding.Location);
        Assert.Equal(2, finding.Evidence.Count);
        Assert.Contains(reference.Fact, finding.Evidence);
        Assert.Contains(semantic.Fact, finding.Evidence);
    }

    /// <summary>Reporting severity and quality gates operate after discovery, preserving the finding's identity.</summary>
    /// <param name="severity">The configured reporting level.</param>
    [Theory]
    [InlineData(DiagnosticSeverity.Info)]
    [InlineData(DiagnosticSeverity.Warning)]
    [InlineData(DiagnosticSeverity.Error)]
    public async Task Selected_rule_uses_policy_severity_without_changing_discovery(DiagnosticSeverity severity)
    {
        var context = Context([Evidence(Orders, Internal)], severity: severity);
        var result = await AnalyzeAsync(context);
        var diagnostic = Assert.Single(result.Diagnostics);
        var raw = Assert.Single((await new ModuleDependenciesRule().EvaluateAsync(context)).Violations);

        Assert.Equal(severity, diagnostic.Severity);
        Assert.Equal(raw.Id, diagnostic.Id);
        Assert.Equal(severity < DiagnosticSeverity.Error, context.Repository!.Policy.Gate.Passes(result));
    }

    /// <summary>Omitting AG001 disables its evaluation through the existing explicit rule-selection contract.</summary>
    [Fact]
    public async Task Omitted_rule_is_not_evaluated()
    {
        var context = Context([Evidence(Orders, Internal)], selectedRule: "AG002");
        IArchitectureRule<DependencyBuildResult>[] available = [new ModuleDependenciesRule(), new CleanLayerRule()];
        var policy = context.Repository!.Policy;
        var selected = policy.SelectRules(available);
        var result = await new AnalysisService<DependencyBuildResult>(selected).AnalyzeAsync(new(context, policy.SeverityPolicy));

        Assert.Equal("AG002", Assert.Single(selected).Descriptor.Id);
        Assert.Equal(AnalysisStatus.Completed, result.Status);
        Assert.Empty(result.Diagnostics);
    }

    /// <summary>A graph from a broader inventory cannot silently acquire completed coverage for a narrower policy scope.</summary>
    /// <param name="source">An unmapped source or an in-scope source.</param>
    /// <param name="target">An unmapped target or an in-scope target.</param>
    [Theory]
    [InlineData("Missing.csproj", Internal)]
    [InlineData(Orders, "Missing.csproj")]
    public async Task Unmapped_evidence_preserves_partial_findings_and_prevents_clean_coverage(string source, string target)
    {
        var context = Context([Evidence(source, target), Evidence(Orders, Internal)]);
        var result = await AnalyzeAsync(context);

        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Single(result.Diagnostics);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("dependency-project-unmapped", issue.Code);
        Assert.Equal("AG001", issue.RuleId);
        Assert.Contains("Missing.csproj", issue.Message);
        Assert.False(context.Repository!.Policy.Gate.Passes(result));
    }

    /// <summary>Extraction failures stay visible even when the available graph supports useful findings.</summary>
    /// <param name="status">An upstream coverage outcome that must be preserved.</param>
    [Theory]
    [InlineData(AnalysisStatus.Incomplete)]
    [InlineData(AnalysisStatus.Failed)]
    [InlineData(AnalysisStatus.Cancelled)]
    public async Task Upstream_coverage_is_preserved_with_partial_findings(AnalysisStatus status)
    {
        var complete = Context([Evidence(Orders, Internal)]);
        var issue = new AnalysisIssue("extraction-coverage", "The supplied graph has partial coverage.");
        var context = new DependencyBuildResult(complete.Repository, complete.Graph, status, [issue]);
        var result = await AnalyzeAsync(context);

        Assert.Equal(status, result.Status);
        Assert.Single(result.Diagnostics);
        Assert.Equal(issue, Assert.Single(result.Issues));
        Assert.False(context.Repository!.Policy.Gate.Passes(result));
    }

    /// <summary>Missing repository or graph context never turns a loading failure into completed evaluation.</summary>
    /// <param name="missingRepository">Whether to omit ownership instead of the evidence graph.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_context_remains_incomplete(bool missingRepository)
    {
        var complete = Context([]);
        var issue = new AnalysisIssue("missing-context", "Dependency extraction could not establish context.");
        var context = new DependencyBuildResult(missingRepository ? null : complete.Repository,
            missingRepository ? complete.Graph : null, AnalysisStatus.Incomplete, [issue]);
        var result = await new ModuleDependenciesRule().EvaluateAsync(context);

        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Empty(result.Violations);
        Assert.Equal(issue, Assert.Single(result.Issues));
    }

    /// <summary>Honors cancellation at the rule boundary and rejects absent input.</summary>
    [Fact]
    public async Task Cancellation_is_observed_and_null_input_is_rejected()
    {
        var rule = new ModuleDependenciesRule();
        await Assert.ThrowsAsync<ArgumentNullException>(() => rule.EvaluateAsync(null!));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => rule.EvaluateAsync(Context([]), cancellation.Token));
        var result = await new AnalysisService<DependencyBuildResult>([rule]).AnalyzeAsync(new(Context([])), cancellation.Token);
        Assert.Equal(AnalysisStatus.Cancelled, result.Status);
    }

    private static Task<AnalysisResult> AnalyzeAsync(DependencyBuildResult context)
    {
        var policy = context.Repository!.Policy;
        var selected = policy.SelectRules<DependencyBuildResult>([new ModuleDependenciesRule()]);
        return new AnalysisService<DependencyBuildResult>(selected).AnalyzeAsync(new(context, policy.SeverityPolicy));
    }

    private static DependencyBuildResult Context(IEnumerable<DependencyEvidence> evidence, string? permission = "contract:Billing/api",
        DependencyKind kind = DependencyKind.Project, DiagnosticSeverity severity = DiagnosticSeverity.Error, string selectedRule = "AG001")
    {
        var policy = new ArchitecturePolicy([
            new("Orders", ["Orders/*.csproj"], allowedDependencies: permission is null ? [] : [permission]),
            new("Billing", ["Billing/*.csproj"], [new("api", [Contracts]), new("events", [OtherContract])], ["module:Shipping"]),
            new("Shipping", [Shipping]),
        ], [new("All", ["**/*.csproj"])], new Dictionary<string, DiagnosticSeverity> { [selectedRule] = severity });
        var repository = new RepositoryModel("App.slnx", policy,
            new[] { Orders, Contracts, Internal, OtherContract, Shipping }.Select(path => new RepositoryProject(path, path, new(path, path, []))));
        // Aggregate-level nodes deliberately have no project path: the original evidence owns that context.
        var edges = evidence.GroupBy(item => (item.SourceProject, item.TargetProject)).Select(group => Edge(kind, group));
        return new(repository, new DependencyGraph([], edges), AnalysisStatus.Completed);
    }

    private static DependencyEdge Edge(DependencyKind kind, IEnumerable<DependencyEvidence> evidence)
    {
        var facts = evidence.ToArray();
        if (kind is DependencyKind.Module or DependencyKind.Layer)
        {
            // A logical edge may combine permitted and forbidden project pairs.
            return new(new(kind, "source-group", "source-group"), new(kind, "target-group", "target-group"), facts);
        }

        return new(new(kind, facts[0].SourceProject, facts[0].SourceProject, facts[0].SourceProject),
            new(kind, facts[0].TargetProject ?? "external", facts[0].TargetProject ?? "external", facts[0].TargetProject), facts);
    }

    private static DependencyEvidence Evidence(string source, string? target) =>
        new(source, target, new($"Project '{source}' references '{target ?? "external"}'."), projectReference: target);

    /// <summary>Supplies an independent selected rule to exercise omission without an empty rule registration.</summary>
    private sealed class CleanLayerRule : IArchitectureRule<DependencyBuildResult>
    {
        /// <inheritdoc/>
        public RuleDescriptor Descriptor => RuleCatalog.LayerDependencies;

        /// <inheritdoc/>
        public Task<RuleEvaluationResult> EvaluateAsync(DependencyBuildResult context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RuleEvaluationResult(AnalysisStatus.Completed));
    }
}
