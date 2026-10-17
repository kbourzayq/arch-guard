using ArchitectureGuardian.Core.Canonicalization;
using ArchitectureGuardian.Core.Repositories;

namespace ArchitectureGuardian.Core.Dependencies;

/// <summary>An immutable evidence graph with deterministic traversal, aggregation and cycle discovery.</summary>
public sealed class DependencyGraph
{
    private readonly Dictionary<string, DependencyNode> _nodes;
    private readonly Dictionary<string, DependencyEdge[]> _outgoing;
    private readonly Dictionary<string, DependencyEdge[]> _incoming;

    /// <summary>Snapshots endpoints, merges duplicate edge pairs and rejects conflicting endpoint identities.</summary>
    /// <param name="nodes">Available endpoints, including isolated nodes.</param>
    /// <param name="edges">Direct dependencies; their endpoints are also included.</param>
    public DependencyGraph(IEnumerable<DependencyNode> nodes, IEnumerable<DependencyEdge> edges)
    {
        var dependencies = CanonicalData.Snapshot(edges, edge => edge.Id);
        var endpoints = CanonicalData.Snapshot(nodes, node => node.Id)
            .Concat(dependencies.SelectMany(edge => new[] { edge.Source, edge.Target })).ToArray();
        if (endpoints.GroupBy(node => node.Id, StringComparer.Ordinal).Any(group => group.Distinct().Count() != 1))
        {
            throw new ArgumentException("Graph endpoint identities must have consistent data.", nameof(nodes));
        }

        Nodes = CanonicalData.Snapshot(endpoints.Distinct(), node => node.Id);
        Edges = CanonicalData.Snapshot(dependencies.GroupBy(edge => edge.Id, StringComparer.Ordinal)
            .Select(group => new DependencyEdge(group.First().Source, group.First().Target,
                group.SelectMany(edge => edge.Evidence))), edge => edge.Id);
        _nodes = Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        _outgoing = Nodes.ToDictionary(node => node.Id, _ => Array.Empty<DependencyEdge>(), StringComparer.Ordinal);
        _incoming = Nodes.ToDictionary(node => node.Id, _ => Array.Empty<DependencyEdge>(), StringComparer.Ordinal);
        foreach (var group in Edges.GroupBy(edge => edge.Source.Id, StringComparer.Ordinal))
        {
            _outgoing[group.Key] = group.OrderBy(edge => edge.Target.Id, StringComparer.Ordinal).ToArray();
        }

        foreach (var group in Edges.GroupBy(edge => edge.Target.Id, StringComparer.Ordinal))
        {
            _incoming[group.Key] = group.OrderBy(edge => edge.Source.Id, StringComparer.Ordinal).ToArray();
        }
    }

    /// <summary>Gets all endpoints in ordinal graph-identity order.</summary>
    public IReadOnlyList<DependencyNode> Nodes { get; }
    /// <summary>Gets all direct dependencies in ordinal edge-identity order.</summary>
    public IReadOnlyList<DependencyEdge> Edges { get; }

    /// <summary>Gets direct outgoing dependencies in ordinal target-identity order.</summary>
    /// <param name="nodeId">An endpoint identity present in the graph.</param>
    /// <returns>An immutable list of outgoing dependencies.</returns>
    /// <exception cref="KeyNotFoundException">The endpoint is unknown.</exception>
    public IReadOnlyList<DependencyEdge> Outgoing(string nodeId) => Array.AsReadOnly(_outgoing[nodeId]);

    /// <summary>Traverses reachable endpoints breadth first, including the start and visiting ordinal neighbors once.</summary>
    /// <param name="nodeId">The starting endpoint identity.</param>
    /// <param name="cancellationToken">Cooperative traversal cancellation.</param>
    /// <returns>The immutable traversal order.</returns>
    public IReadOnlyList<DependencyNode> Traverse(string nodeId, CancellationToken cancellationToken = default)
    {
        var result = new List<DependencyNode>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { _nodes[nodeId].Id };
        var pending = new Queue<string>();
        pending.Enqueue(nodeId);
        while (pending.TryDequeue(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(_nodes[current]);
            foreach (var edge in _outgoing[current])
            {
                if (visited.Add(edge.Target.Id))
                {
                    pending.Enqueue(edge.Target.Id);
                }
            }
        }

        return result.AsReadOnly();
    }

    /// <summary>Finds cyclic strongly connected components, including self loops, without enumerating every simple cycle.</summary>
    /// <param name="kind">The architectural level to inspect.</param>
    /// <param name="cancellationToken">Cooperative cycle-discovery cancellation.</param>
    /// <returns>Components and their members in ordinal node-identity order; member order is not a cycle path.</returns>
    public IReadOnlyList<IReadOnlyList<DependencyNode>> FindCycles(DependencyKind kind, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var finished = new List<string>();
        // Iterative Kosaraju traversal avoids exhausting the call stack on large repositories.
        foreach (var node in Nodes.Where(node => node.Kind == kind))
        {
            var pending = new Stack<(string Id, bool Exit)>();
            pending.Push((node.Id, false));
            while (pending.TryPop(out var entry))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Exit)
                {
                    finished.Add(entry.Id);
                }
                else if (visited.Add(entry.Id))
                {
                    pending.Push((entry.Id, true));
                    foreach (var edge in _outgoing[entry.Id].Reverse())
                    {
                        pending.Push((edge.Target.Id, false));
                    }
                }
            }
        }

        visited.Clear();
        var cycles = new List<IReadOnlyList<DependencyNode>>();
        foreach (var id in finished.AsEnumerable().Reverse())
        {
            if (!visited.Add(id))
            {
                continue;
            }

            var members = new List<DependencyNode>();
            var pending = new Stack<string>();
            pending.Push(id);
            while (pending.TryPop(out var current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                members.Add(_nodes[current]);
                foreach (var edge in _incoming[current])
                {
                    if (visited.Add(edge.Source.Id))
                    {
                        pending.Push(edge.Source.Id);
                    }
                }
            }

            if (members.Count > 1 || _outgoing[id].Any(edge => edge.Target.Id == id))
            {
                cycles.Add(CanonicalData.Snapshot(members, node => node.Id));
            }
        }

        return cycles.OrderBy(cycle => cycle[0].Id, StringComparer.Ordinal).ToList().AsReadOnly();
    }

    /// <summary>Aggregates direct repository dependencies across module and layer boundaries, retaining every origin.</summary>
    /// <param name="repository">The ownership mapping for the source projects in this graph.</param>
    /// <returns>A new graph including isolated module/layer nodes and their cross-boundary edges.</returns>
    /// <remarks>External metadata and intra-module/intra-layer edges do not create logical aggregate edges.</remarks>
    public DependencyGraph Aggregate(RepositoryModel repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var modules = repository.Modules.ToDictionary(module => module.Id,
            module => new DependencyNode(DependencyKind.Module, module.Id, module.Id), StringComparer.Ordinal);
        var layers = repository.Layers.ToDictionary(layer => layer.Id,
            layer => new DependencyNode(DependencyKind.Layer, layer.Id, layer.Id), StringComparer.Ordinal);
        var edges = Edges.ToList();
        foreach (var edge in Edges.Where(edge => edge.Kind is not (DependencyKind.Module or DependencyKind.Layer)))
        {
            if (edge.Source.ProjectPath is not { } source || edge.Target.ProjectPath is not { } target)
            {
                continue;
            }

            var from = repository.ResolvedPolicy.Projects[source];
            var to = repository.ResolvedPolicy.Projects[target];
            if (from.ModuleId != to.ModuleId)
            {
                edges.Add(new(modules[from.ModuleId], modules[to.ModuleId], edge.Evidence));
            }

            if (from.LayerId != to.LayerId)
            {
                edges.Add(new(layers[from.LayerId], layers[to.LayerId], edge.Evidence));
            }
        }

        return new(Nodes.Concat(modules.Values).Concat(layers.Values), edges);
    }
}
