namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Canonical rule metadata with explicit policy selection and solution-engine availability.</summary>
/// <param name="Id">Canonical case-sensitive rule ID.</param>
/// <param name="Description">Meaning of the check.</param>
/// <param name="DocumentationUrl">Canonical rule documentation URL.</param>
/// <param name="Selected">Whether configured in this policy.</param>
/// <param name="EffectiveSeverity">Configured reporting level, or null when deselected.</param>
/// <param name="Scope">Engine scope required for evaluation.</param>
/// <param name="Available">Whether the shared solution engine registers this implementation.</param>
internal sealed record RuleContext(
    string Id,
    string Description,
    string DocumentationUrl,
    bool Selected,
    string? EffectiveSeverity,
    string Scope,
    bool Available);
