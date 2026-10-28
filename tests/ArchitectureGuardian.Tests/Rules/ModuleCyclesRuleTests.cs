using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Repositories;
using ArchitectureGuardian.Core.Rules;
using Xunit;

namespace ArchitectureGuardian.Tests.Rules;

/// <summary>Verifies AG003 cycle fixtures, reproducible paths, permissions, evidence and coverage.</summary>
public sealed class ModuleCyclesRuleTests
{
    /// <summary>Exercises two-module, longer, disconnected and acyclic graph fixtures.</summary>
    /// <param name="edges">Directed module pairs in the fixture.</param>
    /// <param name="paths">Expected representative paths, separated by semicolons.</param>
    [Theory]
    [InlineData("A>B;B>A", "A → B → A")]
    [InlineData("A>C;C>B;B>A", "A → C → B → A")]
    [InlineData("A>B;B>A;B>C;C>D;D>E;E>C;F>G", "A → B → A;C → D → E → C")]
    [InlineData("A>B;A>C;B>D;C>D", "")]
    [InlineData("A>A;A>B;B>B", "")]
    [InlineData("", "")]
    public async Task Graph_fixtures_report_real_cycle_paths(string edges, string paths)
    {
        var context = Context(Facts(edges));
        var result = await new ModuleCyclesRule().EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisStatus.Completed, result.Status);
        Assert.Empty(result.Issues);
        Assert.Equal(paths.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(path => $"Module dependency cycle: {path}.").Order(),
            result.Violations.Select(item => item.Message).Order());
        Assert.All(result.Violations, finding =>
        {
            Assert.Equal("AG003", finding.Rule.Id);
            Assert.Equal(RuleCatalog.ModuleCycles.DocumentationUrl, finding.Rule.DocumentationUrl);
            Assert.NotEmpty(finding.Remediation);
            Assert.Null(finding.Location);
            Assert.Equal(finding.Target.Module, finding.Message.Split(": ")[1].Split(" → ")[0]);
        });
    }

    /// <summary>Chooses an actual shortest return path, resolving ties by module name rather than input order.</summary>
    [Fact]
    public async Task Complex_component_reports_one_concise_deterministic_path_with_only_its_evidence()
    {
        var facts = Facts("A>D;D>E;E>A;A>C;C>A;A>B;B>A;C>B");
        var first = await new ModuleCyclesRule().EvaluateAsync(Context(facts), TestContext.Current.CancellationToken);
        var second = await new ModuleCyclesRule().EvaluateAsync(Context(facts.Reverse()), TestContext.Current.CancellationToken);
        var finding = Assert.Single(first.Violations);

        Assert.Equal("Module dependency cycle: A → B → A.", finding.Message);
        Assert.Equal(finding.Id, Assert.Single(second.Violations).Id);
        Assert.Equal(2, finding.Evidence.Count);
        Assert.All(Facts("A>B;B>A"), item => Assert.Contains(item.Fact, finding.Evidence));
    }

    /// <summary>Deferred evidence collection retains all project pairs on chosen hops across independent cycles.</summary>
    [Fact]
    public async Task Cycle_paths_keep_all_hop_evidence_across_projects_and_graph_levels()
    {
        var firstHop = new DependencyEvidence("A/Contracts.csproj", "B/Implementation.csproj",
            new("Additional A to B use.", new("A/Use.cs", 2, 3)), "A.Use", "B.Service");
        var secondHop = new DependencyEvidence("D/Contracts.csproj", "E/Implementation.csproj",
            new("Additional D to E use.", new("D/Use.cs", 4, 5)), "D.Use", "E.Service");
        var facts = Facts("A>B;B>A;A>C;C>A;D>E;E>D;F>G").Concat([firstHop, secondHop,
            new(firstHop.SourceProject, firstHop.TargetProject, firstHop.Fact, "A.OtherUse", "B.Service")]).ToArray();
        var context = Context(facts);
        var mixed = new DependencyGraph(context.Graph!.Nodes,
            context.Graph.Edges.Concat(Context(facts.Reverse(), DependencyKind.Type).Graph!.Edges)).Aggregate(context.Repository!);
        var direct = await new ModuleCyclesRule().EvaluateAsync(context, TestContext.Current.CancellationToken);
        var aggregated = await new ModuleCyclesRule().EvaluateAsync(new(context.Repository, mixed, AnalysisStatus.Completed), TestContext.Current.CancellationToken);

        Assert.Equal(2, direct.Violations.Count);
        Assert.Equal(direct.Violations.Select(item => item.Id), aggregated.Violations.Select(item => item.Id));
        foreach (var (module, pair, extra) in new[] { ("A", "A>B;B>A", firstHop), ("D", "D>E;E>D", secondHop) })
        {
            var finding = Assert.Single(direct.Violations, item => item.Target.Module == module);
            Assert.Equal(3, finding.Evidence.Count);
            Assert.All(Facts(pair).Select(item => item.Fact).Append(extra.Fact), fact => Assert.Contains(fact, finding.Evidence));
            Assert.Equal(extra.Fact.Location, finding.Location);
        }
    }

    /// <summary>Original ownership determines cycles at every input graph level, without requiring aggregate nodes.</summary>
    /// <param name="kind">The graph level carrying the observed project evidence.</param>
    [Theory]
    [InlineData(DependencyKind.Project)]
    [InlineData(DependencyKind.Assembly)]
    [InlineData(DependencyKind.Namespace)]
    [InlineData(DependencyKind.Type)]
    [InlineData(DependencyKind.Module)]
    [InlineData(DependencyKind.Layer)]
    public async Task Repeated_edges_and_graph_levels_do_not_duplicate_findings_or_evidence(DependencyKind kind)
    {
        var location = new SourceLocation("A/Use.cs", 3, 5, 3, 15);
        var semantic = new DependencyEvidence("A/Implementation.csproj", "B/Contracts.csproj",
            new("A.Use invokes B.Api.", location), "A.Use", "B.Api");
        var facts = Facts("A>B;B>A").Append(semantic).ToArray();
        var context = Context(facts.Concat(facts), kind);
        var graph = new DependencyGraph(context.Graph!.Nodes, context.Graph.Edges.Concat(Context(facts).Graph!.Edges))
            .Aggregate(context.Repository!);
        var finding = Assert.Single((await new ModuleCyclesRule().EvaluateAsync(new(context.Repository, graph, AnalysisStatus.Completed), TestContext.Current.CancellationToken)).Violations);
        var direct = Assert.Single((await new ModuleCyclesRule().EvaluateAsync(Context(facts), TestContext.Current.CancellationToken)).Violations);

        Assert.Equal(direct.Id, finding.Id);
        Assert.Equal(3, finding.Evidence.Count);
        Assert.Contains(semantic.Fact, finding.Evidence);
        Assert.Equal(location, finding.Location);
    }

    /// <summary>Explicit module and public-contract permissions leave observed cycles visible to AG003.</summary>
    /// <param name="contractsOnly">Whether permissions authorize exact public contracts or whole modules.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Allowed_dependencies_still_form_cycles(bool contractsOnly)
    {
        var context = Context(Facts("A>B;B>A"), contractsOnly: contractsOnly);
        Assert.All(context.Graph!.Edges.SelectMany(edge => edge.Evidence),
            fact => Assert.True(context.Repository!.ResolvedPolicy.AllowsModuleDependency(fact.SourceProject, fact.TargetProject!)));
        Assert.Empty((await new ModuleDependenciesRule().EvaluateAsync(context, TestContext.Current.CancellationToken)).Violations);
        Assert.Single((await new ModuleCyclesRule().EvaluateAsync(context, TestContext.Current.CancellationToken)).Violations);
    }

    /// <summary>Same-module project loops, explicit aggregate self edges and external metadata do not form module cycles.</summary>
    [Fact]
    public async Task Internal_and_external_dependencies_are_excluded()
    {
        var facts = Facts("A>A").Concat([
            new("A/Contracts.csproj", "A/Implementation.csproj", new("Reciprocal dependency within A.")),
            new("A/Implementation.csproj", "A/Implementation.csproj", new("Same-project dependency.")),
            new("A/Implementation.csproj", null, new("Uses framework metadata.")),
        ]);
        var context = Context(facts, DependencyKind.Module);
        var result = await new ModuleCyclesRule().EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisStatus.Completed, result.Status);
        Assert.Empty(result.Violations);
    }

    /// <summary>Unmapped project evidence cannot silently establish complete coverage; valid cycles survive.</summary>
    /// <param name="source">Whether the unknown project is the source rather than target.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unmapped_projects_preserve_partial_findings(bool source)
    {
        var unknown = new DependencyEvidence(source ? "Missing.csproj" : "A/Implementation.csproj",
            source ? "A/Contracts.csproj" : "Missing.csproj", new("Unmapped dependency."));
        var context = Context(Facts("A>B;B>A").Concat([unknown, unknown]));
        var result = await new ModuleCyclesRule().EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Single(result.Violations);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("dependency-project-unmapped", issue.Code);
        Assert.Equal("AG003", issue.RuleId);
    }

    /// <summary>Preserves extraction coverage, issues and useful cycle findings.</summary>
    /// <param name="status">The upstream outcome.</param>
    [Theory]
    [InlineData(AnalysisStatus.Incomplete)]
    [InlineData(AnalysisStatus.Failed)]
    [InlineData(AnalysisStatus.Cancelled)]
    public async Task Upstream_coverage_is_preserved(AnalysisStatus status)
    {
        var complete = Context(Facts("A>B;B>A"));
        var issue = new AnalysisIssue("partial-graph", "Extraction did not complete.");
        var result = await new ModuleCyclesRule().EvaluateAsync(new(complete.Repository, complete.Graph, status, [issue]), TestContext.Current.CancellationToken);

        Assert.Equal(status, result.Status);
        Assert.Equal(issue, Assert.Single(result.Issues));
        Assert.Single(result.Violations);
    }

    /// <summary>Missing graph or repository context stays incomplete.</summary>
    /// <param name="repositoryMissing">Whether ownership is missing rather than graph facts.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_context_is_preserved(bool repositoryMissing)
    {
        var complete = Context([]);
        var issue = new AnalysisIssue("missing-context", "Dependency extraction has no context.");
        var result = await new ModuleCyclesRule().EvaluateAsync(new(repositoryMissing ? null : complete.Repository,
            repositoryMissing ? complete.Graph : null, AnalysisStatus.Incomplete, [issue]), TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Equal(issue, Assert.Single(result.Issues));
        Assert.Empty(result.Violations);
    }

    /// <summary>Uses existing selection, severity and gates independently of cycle discovery.</summary>
    /// <param name="severity">The reporting level chosen by the host policy.</param>
    [Theory]
    [InlineData(DiagnosticSeverity.Info)]
    [InlineData(DiagnosticSeverity.Warning)]
    [InlineData(DiagnosticSeverity.Error)]
    public async Task Policy_composition_applies_severity_and_gates(DiagnosticSeverity severity)
    {
        var context = Context(Facts("A>B;B>A"));
        var policy = context.Repository!.Policy;
        var selected = policy.SelectRules<DependencyBuildResult>([new ModuleCyclesRule(), new ModuleDependenciesRule()]);
        Assert.Equal("AG003", Assert.Single(selected).Descriptor.Id);
        var service = new AnalysisService<DependencyBuildResult>(selected);
        var result = await service.AnalyzeAsync(new(context, new(new Dictionary<string, DiagnosticSeverity> { ["AG003"] = severity })), TestContext.Current.CancellationToken);
        var finding = Assert.Single((await new ModuleCyclesRule().EvaluateAsync(context, TestContext.Current.CancellationToken)).Violations);
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(finding.Id, diagnostic.Id);
        Assert.Equal(severity, diagnostic.Severity);
        Assert.Equal(severity < DiagnosticSeverity.Error, policy.Gate.Passes(result));
        var otherPolicy = new ArchitecturePolicy(policy.Modules, policy.Layers,
            new Dictionary<string, DiagnosticSeverity> { ["AG001"] = DiagnosticSeverity.Error });
        Assert.IsType<ModuleDependenciesRule>(Assert.Single(otherPolicy.SelectRules<DependencyBuildResult>([new ModuleCyclesRule(), new ModuleDependenciesRule()])));
    }

    /// <summary>Large cycles use iterative graph and path traversal.</summary>
    [Fact]
    public async Task Deep_cycle_does_not_require_recursion()
    {
        var names = Enumerable.Range(0, 2000).Select(index => $"M{index:D4}").ToArray();
        // Stress cycle traversal, without the unrelated contract/glob inventory used by permission cases.
        var policy = new ArchitecturePolicy(names.Select(name => new ModulePolicy(name, [name + ".csproj"])),
            [new("All", ["**/*.csproj"])], new Dictionary<string, DiagnosticSeverity> { ["AG003"] = DiagnosticSeverity.Error });
        var projects = names.Select(name => new RepositoryProject(name + ".csproj", name, new(name + ".csproj", name, []))).ToArray();
        var repository = new RepositoryModel("App.slnx", policy, projects);
        var nodes = projects.Select(project => new DependencyNode(DependencyKind.Project, project.Id, project.Id, project.Id)).ToArray();
        var edges = nodes.Zip(nodes.Skip(1).Append(nodes[0]), (source, target) => new DependencyEdge(source, target,
            [new(source.ProjectPath!, target.ProjectPath, new($"Project '{source.Identity}' references '{target.Identity}'."),
                projectReference: target.ProjectPath)]));
        var context = new DependencyBuildResult(repository, new DependencyGraph(nodes, edges), AnalysisStatus.Completed);
        var result = await new ModuleCyclesRule().EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisStatus.Completed, result.Status);
        Assert.Equal(names.Length, Assert.Single(result.Violations).Evidence.Count);
    }

    /// <summary>Rejects absent input and observes cooperative cancellation.</summary>
    [Fact]
    public async Task Null_and_cancelled_input_are_rejected()
    {
        var rule = new ModuleCyclesRule();
        await Assert.ThrowsAsync<ArgumentNullException>(() => rule.EvaluateAsync(null!, TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => rule.EvaluateAsync(Context([]), cancellation.Token));
    }

    private static DependencyEvidence[] Facts(string pairs) => pairs.Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(pair => pair.Split('>')).Select(pair => new DependencyEvidence($"{pair[0]}/Implementation.csproj", $"{pair[1]}/Contracts.csproj",
            new($"Project '{pair[0]}/Implementation.csproj' references '{pair[1]}/Contracts.csproj'."), projectReference: $"{pair[1]}/Contracts.csproj")).ToArray();

    private static DependencyBuildResult Context(IEnumerable<DependencyEvidence> evidence, DependencyKind kind = DependencyKind.Project,
        bool contractsOnly = true, bool allowDependencies = true)
    {
        var facts = evidence.ToArray();
        var names = facts.SelectMany(item => new[] { item.SourceProject, item.TargetProject })
            .Where(path => path is not null && path.Contains('/', StringComparison.Ordinal))
            .Select(path => path!.Split('/')[0]).Append("Isolated").Distinct(StringComparer.Ordinal).ToArray();
        var modules = names.Select(name => new ModulePolicy(name, [$"{name}/*.csproj"], [new("api", [$"{name}/Contracts.csproj"])],
            allowedDependencies: allowDependencies ? names.Where(target => target != name)
                .Select(target => contractsOnly ? $"contract:{target}/api" : $"module:{target}") : [])).ToArray();
        var policy = new ArchitecturePolicy(modules, [new("All", ["**/*.csproj"])],
            new Dictionary<string, DiagnosticSeverity> { ["AG003"] = DiagnosticSeverity.Error });
        var projects = names.SelectMany(name => new[] { $"{name}/Implementation.csproj", $"{name}/Contracts.csproj" })
            .Select(path => new RepositoryProject(path, path, new(path, path, [])));
        var repository = new RepositoryModel("App.slnx", policy, projects);
        var edges = facts.Select(item => new DependencyEdge(Node(item.SourceProject), Node(item.TargetProject ?? "external"), [item]));
        return new(repository, new DependencyGraph([], edges), AnalysisStatus.Completed);

        DependencyNode Node(string path) => new(kind, kind == DependencyKind.Module ? path.Split('/')[0] : path, kind == DependencyKind.Module ? path.Split('/')[0] : path,
            kind is DependencyKind.Module or DependencyKind.Layer || path == "external" ? null : path);
    }
}
