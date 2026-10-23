using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Baselines;
using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Analysis.Changes;

/// <summary>Reports combined snapshot coverage and optional classification against a complete base scan.</summary>
public sealed class ChangeAnalysisResult
{
    internal ChangeAnalysisResult(AnalysisResult analysis, string? baseCommit = null,
        BaselineComparison? comparison = null, ArchitecturePolicy? candidatePolicy = null)
    {
        Analysis = analysis;
        BaseCommit = baseCommit;
        Comparison = comparison;
        CandidatePolicy = candidatePolicy;
    }

    /// <summary>Gets candidate diagnostics with coverage issues from both snapshots.</summary>
    public AnalysisResult Analysis { get; }

    /// <summary>Gets the resolved base commit, when reference resolution succeeded.</summary>
    public string? BaseCommit { get; }

    /// <summary>Gets existing/new/resolved findings, or null when the base could not be fully analyzed.</summary>
    public BaselineComparison? Comparison { get; }

    /// <summary>Gets the policy captured from the candidate tree, when it could be loaded.</summary>
    public ArchitecturePolicy? CandidatePolicy { get; }

    /// <summary>Gets whether both snapshots completed and the candidate severity gate passes for new findings.</summary>
    public bool GatePassed => Comparison is not null && CandidatePolicy is not null && CandidatePolicy.Gate.PassesNewFindings(Comparison);
}
