namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>An operational coverage issue, separate from architecture violations.</summary>
/// <param name="Code">Stable issue code.</param>
/// <param name="Message">Actionable coverage detail.</param>
internal sealed record ContextIssue(
    string Code,
    string Message);
