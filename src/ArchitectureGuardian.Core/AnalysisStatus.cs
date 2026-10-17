namespace ArchitectureGuardian.Core;

/// <summary>Describes analysis coverage independently of findings, severity policy and quality gates.</summary>
public enum AnalysisStatus
{
    /// <summary>All requested evaluation completed with no operational issues.</summary>
    Completed,

    /// <summary>Some required context or coverage was unavailable; issues explain the gap.</summary>
    Incomplete,

    /// <summary>An operational failure stopped analysis; issues explain the failure.</summary>
    Failed,

    /// <summary>Evaluation stopped for cancellation and may contain partial findings.</summary>
    Cancelled,
}

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
