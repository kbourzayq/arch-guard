using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Repositories;

namespace ArchitectureGuardian.Core.Dependencies;

/// <summary>Pairs an available dependency graph with loading, normalization and extraction coverage.</summary>
public sealed class DependencyBuildResult
{
    /// <summary>Creates an immutable coverage result; completion requires a graph and no operational issues.</summary>
    /// <param name="repository">The normalized declarations and ownership used to extract the graph.</param>
    /// <param name="graph">The available graph, or null when no graph could be constructed.</param>
    /// <param name="status">The combined coverage outcome.</param>
    /// <param name="issues">Operational issues explaining unfinished coverage.</param>
    public DependencyBuildResult(RepositoryModel? repository, DependencyGraph? graph, AnalysisStatus status, IEnumerable<AnalysisIssue>? issues = null)
    {
        var coverage = new AnalysisResult(status, issues: issues);
        if (status == AnalysisStatus.Completed && (graph is null || repository is null))
        {
            throw new ArgumentException("Completed dependency extraction requires a repository and graph.", nameof(graph));
        }

        Repository = repository;
        Graph = graph;
        Status = coverage.Status;
        Issues = coverage.Issues;
    }

    /// <summary>Gets normalized declarations, policy permissions and ownership for the graph.</summary>
    public RepositoryModel? Repository { get; }
    /// <summary>Gets the available graph; inspect coverage before evaluating or claiming a clean result.</summary>
    public DependencyGraph? Graph { get; }
    /// <summary>Gets combined workspace, normalization and extraction coverage.</summary>
    public AnalysisStatus Status { get; }
    /// <summary>Gets immutable operational issues in canonical order.</summary>
    public IReadOnlyList<AnalysisIssue> Issues { get; }
}
