namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Versioned policy-only rule catalog; completion does not establish workspace coverage.</summary>
/// <param name="SchemaVersion">MCP response schema version.</param>
/// <param name="Status">Policy loading outcome.</param>
/// <param name="Scope">Policy metadata scope.</param>
/// <param name="PolicyPath">Root-relative policy path.</param>
/// <param name="InventoryStatus">Explicitly unresolved project inventory.</param>
/// <param name="Rules">Filtered canonical catalog in ordinal ID order.</param>
internal sealed record RulesResponse(
    int SchemaVersion,
    string Status,
    string Scope,
    string PolicyPath,
    string InventoryStatus,
    IReadOnlyList<RuleContext> Rules);
