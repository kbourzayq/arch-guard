namespace ArchitectureGuardian.Core;

/// <summary>Captures analysis coverage, policy-adjusted diagnostics and operational issues for one run.</summary>
/// <remarks>
/// Completion describes coverage rather than quality-gate success. Partial findings remain useful
/// when analysis is incomplete, fails or is cancelled, but cannot establish a clean result.
/// </remarks>
public sealed class AnalysisResult
{
    /// <summary>Creates an immutable result with reproducibly ordered diagnostics and issues.</summary>
    /// <param name="status">The coverage outcome, independently of diagnostic severity.</param>
    /// <param name="diagnostics">Available diagnostics, including partial findings; null means none.</param>
    /// <param name="issues">Operational problems explaining missing coverage; null means none.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> is undefined.</exception>
    /// <exception cref="ArgumentException">
    /// A collection contains null, a completed result has issues, or an incomplete/failed result has no issues.
    /// </exception>
    public AnalysisResult(AnalysisStatus status, IEnumerable<Diagnostic>? diagnostics = null,
        IEnumerable<AnalysisIssue>? issues = null)
    {
        Diagnostics = CanonicalData.Snapshot(diagnostics ?? [], item => item.SortKey);
        Issues = CanonicalData.Snapshot(issues ?? [], item => item.SortKey);
        AnalysisOutcome.Validate(status, Issues);
        Status = status;
    }

    /// <summary>Gets whether the requested analysis completed, lacked coverage, failed or was cancelled.</summary>
    public AnalysisStatus Status { get; }

    /// <summary>Gets whether analysis completed; this does not indicate policy compliance or gate success.</summary>
    public bool IsComplete => Status == AnalysisStatus.Completed;

    /// <summary>Gets the read-only diagnostics in deterministic canonical order.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>Gets the read-only operational problems in deterministic canonical order.</summary>
    public IReadOnlyList<AnalysisIssue> Issues { get; }
}
