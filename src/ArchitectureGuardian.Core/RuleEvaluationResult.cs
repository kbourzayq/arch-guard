namespace ArchitectureGuardian.Core;

/// <summary>Captures one rule's coverage, severity-free findings and operational issues.</summary>
/// <remarks>Severity policy is applied later; a rule must explicitly report any missing analysis context.</remarks>
public sealed class RuleEvaluationResult
{
    /// <summary>Creates an immutable, reproducibly ordered snapshot of a rule evaluation.</summary>
    /// <param name="status">Whether evaluation completed, lacked context, failed or was cancelled.</param>
    /// <param name="violations">Available findings without severity; null means none.</param>
    /// <param name="issues">Operational problems explaining missing coverage; null means none.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> is undefined.</exception>
    /// <exception cref="ArgumentException">
    /// A collection contains null, a completed evaluation has issues, or an incomplete/failed evaluation has no issues.
    /// </exception>
    public RuleEvaluationResult(AnalysisStatus status, IEnumerable<RuleViolation>? violations = null,
        IEnumerable<AnalysisIssue>? issues = null)
    {
        Violations = CanonicalData.Snapshot(violations ?? [], item => item.SortKey);
        Issues = CanonicalData.Snapshot(issues ?? [], item => item.SortKey);
        AnalysisOutcome.Validate(status, Issues);
        Status = status;
    }

    /// <summary>Gets the coverage outcome independently of how many violations were found.</summary>
    public AnalysisStatus Status { get; }

    /// <summary>Gets the read-only findings in deterministic canonical order, including any partial findings.</summary>
    public IReadOnlyList<RuleViolation> Violations { get; }

    /// <summary>Gets the read-only operational problems in deterministic canonical order.</summary>
    public IReadOnlyList<AnalysisIssue> Issues { get; }
}
