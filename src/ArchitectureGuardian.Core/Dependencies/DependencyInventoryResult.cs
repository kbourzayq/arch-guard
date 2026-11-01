using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Repositories;

namespace ArchitectureGuardian.Core.Dependencies;

/// <summary>Pairs policy-independent declarations and dependency evidence with explicit coverage.</summary>
public sealed class DependencyInventoryResult
{
    /// <summary>Snapshots coverage; completed inventory requires declarations and a graph.</summary>
    /// <param name="inventory">Available source declarations.</param>
    /// <param name="graph">Available evidence, without module/layer aggregation.</param>
    /// <param name="status">Combined workspace and extraction coverage.</param>
    /// <param name="issues">Operational issues explaining missing coverage.</param>
    public DependencyInventoryResult(RepositoryInventory? inventory, DependencyGraph? graph, AnalysisStatus status,
        IEnumerable<AnalysisIssue>? issues = null)
    {
        var coverage = new AnalysisResult(status, issues: issues);
        if (status == AnalysisStatus.Completed && (inventory is null || graph is null))
        {
            throw new ArgumentException("Completed inventory requires declarations and dependency evidence.");
        }
        Inventory = inventory;
        Graph = graph;
        Status = coverage.Status;
        Issues = coverage.Issues;
    }

    /// <summary>Gets available declarations, with no ownership assumptions.</summary>
    public RepositoryInventory? Inventory { get; }
    /// <summary>Gets available dependency evidence.</summary>
    public DependencyGraph? Graph { get; }
    /// <summary>Gets coverage independently of proposal validity or compliance.</summary>
    public AnalysisStatus Status { get; }
    /// <summary>Gets canonically ordered operational issues.</summary>
    public IReadOnlyList<AnalysisIssue> Issues { get; }
}
