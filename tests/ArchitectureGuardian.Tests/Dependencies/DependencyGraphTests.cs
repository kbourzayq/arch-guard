using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Repositories;
using Xunit;

namespace ArchitectureGuardian.Tests.Dependencies;

/// <summary>Verifies graph contracts, immutable evidence and deterministic traversal/cycle/aggregation behavior.</summary>
public sealed class DependencyGraphTests
{
    /// <summary>Merges all origin facts without losing ordering or exposing mutable graph collections.</summary>
    [Fact]
    public void Duplicate_edges_preserve_distinct_evidence_and_snapshot_inputs()
    {
        var a = Node("A");
        var b = Node("B");
        var first = Evidence("first");
        var second = Evidence("second");
        var input = new List<DependencyEvidence> { second, first, first };
        var edge = new DependencyEdge(a, b, input);
        input.Clear();
        var graph = new DependencyGraph([a, b], [edge, new(a, b, [first])]);
        var combined = Assert.Single(graph.Edges);
        Assert.Equal(2, combined.Evidence.Count);
        var reversed = new DependencyGraph([b, a], [new(a, b, [first, second])]);
        Assert.Equal(graph.Nodes, reversed.Nodes);
        Assert.Equal(combined.Evidence, Assert.Single(reversed.Edges).Evidence);
        Assert.Equal(combined.Id, Assert.Single(reversed.Edges).Id);
        Assert.Throws<NotSupportedException>(() => ((IList<DependencyEvidence>)combined.Evidence).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<DependencyEdge>)graph.Outgoing(a.Id)).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<DependencyNode>)graph.Nodes).Clear());
    }

    /// <summary>Deduplication retains origins which share a fact but differ in symbol or bound reference identity.</summary>
    [Fact]
    public void Equal_facts_do_not_collapse_distinct_dependency_origins()
    {
        var fact = new DiagnosticEvidence("use: \ud83d\ude00", new("src/Type.cs", 10, 2));
        var first = new DependencyEvidence("App.csproj", "Library.csproj", fact, "App.A", "Library.Type", targetAssembly: "Library");
        var second = new DependencyEvidence("App.csproj", "Library.csproj", fact, "App.B", "Library.Type", targetAssembly: "Library");
        var third = new DependencyEvidence("App.csproj", "Library.csproj", fact, "App.A", "Library.Type", targetAssembly: "Other");
        var duplicate = new DependencyEvidence("App.csproj", "Library.csproj", new("use: \ud83d\ude00", new("src\\Type.cs", 10, 2)),
            "App.A", "Library.Type", targetAssembly: "Library");
        var inputs = new[] { third, duplicate, second, first, duplicate };
        var edge = new DependencyEdge(Node("A"), Node("B"), inputs);
        var reversed = new DependencyEdge(Node("A"), Node("B"), inputs.Reverse());
        Assert.Equal(3, edge.Evidence.Count);
        Assert.Equal(edge.Evidence, reversed.Evidence);
        Assert.Contains(first, edge.Evidence);
        Assert.Contains(second, edge.Evidence);
        Assert.Contains(third, edge.Evidence);
    }

    /// <summary>Finds strongly connected groups and self loops in a stable order independently of input order.</summary>
    [Fact]
    public void Traversal_and_cycles_are_deterministic_and_keep_direction()
    {
        var nodes = new[] { Node("A"), Node("B"), Node("C"), Node("D"), Node("E"), Node("isolated") };
        var edges = new[] { Edge(nodes[0], nodes[1]), Edge(nodes[1], nodes[2]), Edge(nodes[2], nodes[0]),
            Edge(nodes[2], nodes[3]), Edge(nodes[3], nodes[4]), Edge(nodes[4], nodes[3]), Edge(nodes[5], nodes[5]) };
        var graph = new DependencyGraph(nodes, edges);
        var reversed = new DependencyGraph(nodes.Reverse(), edges.Reverse());
        Assert.Equal(graph.Traverse(nodes[0].Id, TestContext.Current.CancellationToken), reversed.Traverse(nodes[0].Id, TestContext.Current.CancellationToken));
        Assert.Equal(5, graph.Traverse(nodes[0].Id, TestContext.Current.CancellationToken).Count);
        Assert.Equal(2, graph.Traverse(nodes[3].Id, TestContext.Current.CancellationToken).Count);
        var cycles = graph.FindCycles(DependencyKind.Type, TestContext.Current.CancellationToken);
        Assert.Equal(3, cycles.Count);
        Assert.Equal(new[] { 1, 2, 3 }, cycles.Select(cycle => cycle.Count).Order());
        Assert.Equal(cycles.Select(cycle => string.Join(",", cycle.Select(node => node.Id))),
            reversed.FindCycles(DependencyKind.Type, TestContext.Current.CancellationToken).Select(cycle => string.Join(",", cycle.Select(node => node.Id))));
        Assert.Empty(graph.FindCycles(DependencyKind.Module, TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => graph.Traverse(nodes[0].Id, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => graph.FindCycles(DependencyKind.Type, cancellation.Token));
    }

    /// <summary>Traverses a deep acyclic chain without recursive graph algorithms.</summary>
    [Fact]
    public void Deep_graphs_do_not_require_recursive_traversal()
    {
        var nodes = Enumerable.Range(0, 10000).Select(index => Node(index.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var graph = new DependencyGraph(nodes, nodes.Zip(nodes.Skip(1), Edge));
        Assert.Empty(graph.FindCycles(DependencyKind.Type, TestContext.Current.CancellationToken));
        Assert.Equal(nodes.Length, graph.Traverse(nodes[0].Id, TestContext.Current.CancellationToken).Count);
    }

    /// <summary>Aggregates cross-boundary dependencies while keeping contract-sensitive project origins intact.</summary>
    [Fact]
    public void Aggregation_preserves_project_and_symbol_origins_and_excludes_internal_self_edges()
    {
        var projects = new[] { Project("A.csproj"), Project("B.csproj"), Project("C.csproj") };
        var policy = new ArchitecturePolicy([new("First", ["A.csproj", "C.csproj"]), new("Second", ["B.csproj"])],
            [new("Top", ["A.csproj"]), new("Bottom", ["B.csproj", "C.csproj"])],
            new Dictionary<string, DiagnosticSeverity> { ["AG001"] = DiagnosticSeverity.Error });
        var repository = new RepositoryModel("App.slnx", policy, projects);
        var a = new DependencyNode(DependencyKind.Project, "A.csproj", "A", "A.csproj");
        var b = new DependencyNode(DependencyKind.Project, "B.csproj", "B", "B.csproj");
        var c = new DependencyNode(DependencyKind.Project, "C.csproj", "C", "C.csproj");
        var evidence = new DependencyEvidence("A.csproj", "B.csproj", new("reference"), projectReference: "B.csproj");
        var internalEvidence = new DependencyEvidence("A.csproj", "C.csproj", new("source use", new("A.cs", 2, 3)), "A.Type", "C.Type");
        var graph = new DependencyGraph([a, b, c], [new(a, b, [evidence]), new(a, c, [internalEvidence])]).Aggregate(repository);
        var module = Assert.Single(graph.Edges, edge => edge.Kind == DependencyKind.Module);
        Assert.Equal("First", module.Source.Name);
        Assert.Equal("Second", module.Target.Name);
        Assert.Equal(evidence, Assert.Single(module.Evidence));
        var layer = Assert.Single(graph.Edges, edge => edge.Kind == DependencyKind.Layer);
        Assert.Equal(2, layer.Evidence.Count);
        Assert.Contains(internalEvidence, layer.Evidence);
        Assert.Equal(graph.Edges.Select(edge => edge.Id), graph.Aggregate(repository).Edges.Select(edge => edge.Id));
        Assert.False(repository.ResolvedPolicy.AllowsModuleDependency(evidence.SourceProject, evidence.TargetProject!));
    }

    /// <summary>Rejects unsupported contracts and inconsistent completion claims.</summary>
    [Fact]
    public void Invalid_graph_contracts_are_rejected()
    {
        var node = Node("A");
        Assert.Throws<ArgumentException>(() => new DependencyEdge(node, new(DependencyKind.Project, "B", "B"), [Evidence("fact")]));
        Assert.Throws<ArgumentException>(() => new DependencyEdge(node, Node("B"), []));
        Assert.Throws<ArgumentException>(() => new DependencyGraph([node, new(DependencyKind.Type, "A", "different")], []));
        Assert.Throws<ArgumentException>(() => new DependencyBuildResult(null, null, AnalysisStatus.Completed));
        Assert.Throws<ArgumentException>(() => new DependencyBuildResult(null, null, AnalysisStatus.Incomplete));
    }

    private static DependencyNode Node(string name) => new(DependencyKind.Type, name, name);
    private static DependencyEvidence Evidence(string description) => new("App.csproj", null, new(description));
    private static DependencyEdge Edge(DependencyNode source, DependencyNode target) => new(source, target, [Evidence("use")]);
    private static RepositoryProject Project(string path) => new(path, path, new(path, path, [new(path, "", [])]));
}
