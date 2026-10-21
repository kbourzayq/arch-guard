using ArchitectureGuardian.Core.Analysis;

namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Versioned scan/comparison response; unavailable work cannot establish a passing gate.</summary>
/// <param name="SchemaVersion">Response schema version.</param>
/// <param name="Status">Completed, incomplete, failed, cancelled or unsupported.</param>
/// <param name="Scope">Explicit analysis scope, currently solution.</param>
/// <param name="InputPath">Operator-root-relative solution or project path.</param>
/// <param name="PolicyPath">Operator-root-relative policy path.</param>
/// <param name="Configuration">Requested build configuration.</param>
/// <param name="TargetFramework">Explicit framework, or null for input default.</param>
/// <param name="BaseReference">Requested comparison base, or null for a full scan.</param>
/// <param name="GateOutcome">Passed, failed, or not-evaluated when coverage is unavailable.</param>
/// <param name="FailOn">Configured inclusive severity threshold.</param>
/// <param name="Counts">Counts of all diagnostics in this response; null when comparison is unsupported.</param>
/// <param name="Diagnostics">Complete evidence-backed diagnostic payload, never silently truncated.</param>
/// <param name="Issues">Operational coverage problems, separate from findings.</param>
internal sealed record AnalysisResponse(
    int SchemaVersion,
    string Status,
    string Scope,
    string InputPath,
    string PolicyPath,
    string Configuration,
    string? TargetFramework,
    string? BaseReference,
    string GateOutcome,
    string FailOn,
    DiagnosticCounts? Counts,
    IReadOnlyList<AnalysisDiagnostic> Diagnostics,
    IReadOnlyList<AnalysisIssue> Issues);
