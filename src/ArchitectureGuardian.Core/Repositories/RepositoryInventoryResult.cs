using ArchitectureGuardian.Core.Analysis;

namespace ArchitectureGuardian.Core.Repositories;

/// <summary>Pairs policy-independent source declarations with normalization coverage.</summary>
public sealed class RepositoryInventoryResult
{
    /// <summary>Creates a coverage result; completion requires an inventory.</summary>
    /// <param name="inventory">Available source declarations.</param>
    /// <param name="status">Loading and normalization coverage.</param>
    /// <param name="issues">Operational issues explaining missing context.</param>
    public RepositoryInventoryResult(RepositoryInventory? inventory, AnalysisStatus status, IEnumerable<AnalysisIssue>? issues = null)
    {
        var coverage = new AnalysisResult(status, issues: issues);
        if (status == AnalysisStatus.Completed && inventory is null)
        {
            throw new ArgumentException("Completed normalization requires an inventory.");
        }
        Inventory = inventory;
        Status = coverage.Status;
        Issues = coverage.Issues;
    }

    /// <summary>Gets available declarations.</summary>
    public RepositoryInventory? Inventory { get; }
    /// <summary>Gets combined coverage.</summary>
    public AnalysisStatus Status { get; }
    /// <summary>Gets canonically ordered issues.</summary>
    public IReadOnlyList<AnalysisIssue> Issues { get; }
}
