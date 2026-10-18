using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Rules;

/// <summary>Checks AG003 cycles across logical modules using original project ownership.</summary>
/// <remarks>Permissions do not remove observed dependencies; intra-module dependencies are excluded.</remarks>
public sealed class ModuleCyclesRule : IArchitectureRule<DependencyBuildResult>
{
    /// <inheritdoc/>
    public RuleDescriptor Descriptor => RuleCatalog.ModuleCycles;

    /// <summary>Reports one reproducible, evidence-backed cycle path per cyclic strongly connected component.</summary>
    /// <param name="context">The repository, evidence graph and extraction coverage for the supplied scope.</param>
    /// <param name="cancellationToken">Cooperative cancellation during graph construction and traversal.</param>
    /// <returns>Severity-free findings preserving upstream coverage and unmapped-project issues.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupts evaluation.</exception>
    public Task<RuleEvaluationResult> EvaluateAsync(DependencyBuildResult context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Repository is null || context.Graph is null)
        {
            return Task.FromResult(new RuleEvaluationResult(context.Status, issues: context.Issues));
        }

        var policy = context.Repository.ResolvedPolicy;
        var issues = context.Issues.ToList();
        var nodes = context.Repository.Modules.ToDictionary(module => module.Id,
            module => new DependencyNode(DependencyKind.Module, module.Id, module.Id), StringComparer.Ordinal);
        var pairs = new Dictionary<(string Source, string Target), HashSet<DependencyEvidence>>();
        // A reference may occur at several graph levels. Rebuild module edges from its
        // original ownership so direct and aggregated input have identical semantics.
        foreach (var edge in context.Graph.Edges)
        {
            foreach (var evidence in edge.Evidence)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (evidence.TargetProject is not { } target)
                {
                    continue;
                }

                var pair = (evidence.SourceProject, target);
                if (!pairs.TryGetValue(pair, out var facts))
                {
                    facts = [];
                    pairs.Add(pair, facts);
                }

                facts.Add(evidence);
            }
        }

        var edges = new List<DependencyEdge>();
        foreach (var (pair, facts) in pairs.OrderBy(item => item.Key.Source, StringComparer.Ordinal)
            .ThenBy(item => item.Key.Target, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!policy.Projects.TryGetValue(pair.Source, out var source) || !policy.Projects.TryGetValue(pair.Target, out var target))
            {
                issues.Add(new("dependency-project-unmapped",
                    $"Dependency '{pair.Source}' → '{pair.Target}' is outside the supplied repository's policy inventory.", Descriptor.Id));
                continue;
            }

            if (source.ModuleId != target.ModuleId)
            {
                edges.Add(new(nodes[source.ModuleId], nodes[target.ModuleId], facts));
            }
        }

        var graph = new DependencyGraph(nodes.Values, edges);
        var violations = new List<RuleViolation>();
        foreach (var component in graph.FindCycles(DependencyKind.Module, cancellationToken))
        {
            var path = FindPath(graph, component, cancellationToken);
            var evidence = path.SelectMany(edge => edge.Evidence).Select(item => item.Fact).Distinct()
                .OrderBy(fact => fact.CanonicalKey, StringComparer.Ordinal).ToArray();
            var names = path.Select(edge => edge.Source.Identity).Append(path[0].Source.Identity);
            violations.Add(new(Descriptor, $"Module dependency cycle: {string.Join(" → ", names)}.",
                new(module: path[0].Source.Identity), evidence,
                "Break the cycle by removing or inverting a dependency, or moving a shared contract to an independent module. Allowed dependencies do not exempt cycles.",
                evidence.FirstOrDefault(fact => fact.Location is not null)?.Location));
        }

        var status = context.Status == AnalysisStatus.Completed && issues.Count > 0 ? AnalysisStatus.Incomplete : context.Status;
        return Task.FromResult(new RuleEvaluationResult(status, violations, issues));
    }

    private static IReadOnlyList<DependencyEdge> FindPath(DependencyGraph graph,
        IReadOnlyList<DependencyNode> component, CancellationToken cancellationToken)
    {
        var start = component.MinBy(node => node.Identity, StringComparer.Ordinal)!;
        var members = component.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var parents = new Dictionary<string, DependencyEdge>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { start.Id };
        var pending = new Queue<string>();
        pending.Enqueue(start.Id);
        // Breadth first search selects a shortest cycle through the ordinal-first
        // member. Ordinal module names resolve ties, independent of graph hashes.
        while (pending.TryDequeue(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var edge in graph.Outgoing(current).OrderBy(edge => edge.Target.Identity, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!members.Contains(edge.Target.Id))
                {
                    continue;
                }

                if (edge.Target.Id == start.Id)
                {
                    var path = new List<DependencyEdge> { edge };
                    while (current != start.Id)
                    {
                        var parent = parents[current];
                        path.Add(parent);
                        current = parent.Source.Id;
                    }

                    path.Reverse();
                    return path;
                }

                if (visited.Add(edge.Target.Id))
                {
                    parents.Add(edge.Target.Id, edge);
                    pending.Enqueue(edge.Target.Id);
                }
            }
        }

        throw new InvalidOperationException("A cyclic component must contain a return path to each member.");
    }
}
