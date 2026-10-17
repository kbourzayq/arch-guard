namespace ArchitectureGuardian.Core.Analysis;

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
