namespace ArchitectureGuardian.Core.Analysis;

/// <summary>Enforces shared coverage invariants for rule-level and run-level results.</summary>
internal static class AnalysisOutcome
{
    /// <summary>Checks that the outcome and its operational issues tell a consistent coverage story.</summary>
    /// <param name="status">The proposed coverage outcome.</param>
    /// <param name="issues">The operational problems attached to that outcome.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> is undefined.</exception>
    /// <exception cref="ArgumentException">
    /// Completed analysis has issues, or incomplete/failed analysis lacks an explanatory issue.
    /// </exception>
    public static void Validate(AnalysisStatus status, IReadOnlyList<AnalysisIssue> issues)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        // Operational issues represent missing coverage, so they cannot accompany completion.
        if (status == AnalysisStatus.Completed && issues.Count != 0)
        {
            throw new ArgumentException("Completed analysis cannot contain operational issues.", nameof(issues));
        }

        // Cancellation can stand alone; other unfinished outcomes must explain what went wrong.
        if (status is AnalysisStatus.Incomplete or AnalysisStatus.Failed && issues.Count == 0)
        {
            throw new ArgumentException("Incomplete or failed analysis must explain the operational problem.", nameof(issues));
        }
    }
}
