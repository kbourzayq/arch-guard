namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Versioned resolved architecture context, preserving normalization coverage and issues.</summary>
/// <param name="SchemaVersion">MCP response schema version.</param>
/// <param name="Status">Completed, incomplete, failed or cancelled normalization.</param>
/// <param name="Scope">Repository context scope; this is not a rule scan.</param>
/// <param name="PolicyPath">Root-relative policy path.</param>
/// <param name="InputPath">Root-relative input path.</param>
/// <param name="Configuration">Evaluated build configuration.</param>
/// <param name="TargetFramework">Explicit framework, or null for the input default.</param>
/// <param name="Module">Requested exact module filter.</param>
/// <param name="Layer">Requested exact layer filter.</param>
/// <param name="Issues">Operational coverage issues, preserved even when filtering.</param>
/// <param name="Modules">Filtered modules, membership and constraints.</param>
/// <param name="Layers">Filtered layers, membership and constraints.</param>
/// <param name="Projects">Filtered resolved project ownership.</param>
/// <param name="Boundaries">Shared permission and implementation-boundary semantics.</param>
/// <param name="Rules">Policy selection, reporting severities and availability.</param>
/// <param name="FailOn">Configured quality gate threshold; no gate is evaluated.</param>
internal sealed record ArchitectureResponse(
    int SchemaVersion,
    string Status,
    string Scope,
    string PolicyPath,
    string InputPath,
    string Configuration,
    string? TargetFramework,
    string? Module,
    string? Layer,
    IReadOnlyList<ContextIssue> Issues,
    IReadOnlyList<ModuleContext> Modules,
    IReadOnlyList<LayerContext> Layers,
    IReadOnlyList<ProjectContext> Projects,
    IReadOnlyList<string> Boundaries,
    IReadOnlyList<RuleContext> Rules,
    string FailOn);
