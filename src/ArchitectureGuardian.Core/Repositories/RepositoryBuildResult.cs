using ArchitectureGuardian.Core.Analysis;

namespace ArchitectureGuardian.Core.Repositories;

/// <summary>Pairs a normalized model with explicit workspace and normalization coverage.</summary>
public sealed class RepositoryBuildResult
{
    /// <summary>Creates an immutable result; completion requires a model and no operational issues.</summary>
    /// <param name="model">The available model, or null when normalization could not produce one.</param>
    /// <param name="status">The aggregate coverage outcome.</param>
    /// <param name="issues">Loading and normalization issues, or null for none.</param>
    /// <exception cref="ArgumentException">The coverage is inconsistent or completed coverage has no model.</exception>
    public RepositoryBuildResult(RepositoryModel? model, AnalysisStatus status, IEnumerable<AnalysisIssue>? issues = null)
    {
        var coverage = new AnalysisResult(status, issues: issues);
        if (status == AnalysisStatus.Completed && model is null)
        {
            throw new ArgumentException("Completed repository normalization requires a model.", nameof(model));
        }

        Model = model;
        Status = coverage.Status;
        Issues = coverage.Issues;
    }

    /// <summary>Gets the available model; inspect coverage before evaluating it.</summary>
    public RepositoryModel? Model { get; }
    /// <summary>Gets combined loading and normalization coverage, independently of policy compliance.</summary>
    public AnalysisStatus Status { get; }
    /// <summary>Gets immutable operational issues in canonical order.</summary>
    public IReadOnlyList<AnalysisIssue> Issues { get; }
}
