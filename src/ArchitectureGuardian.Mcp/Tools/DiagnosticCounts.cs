namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Counts of available findings, independently of completion and gate decisions.</summary>
/// <param name="Total">All diagnostics.</param>
/// <param name="Info">Information diagnostics.</param>
/// <param name="Warning">Warning diagnostics.</param>
/// <param name="Error">Error diagnostics.</param>
internal sealed record DiagnosticCounts(int Total, int Info, int Warning, int Error);
