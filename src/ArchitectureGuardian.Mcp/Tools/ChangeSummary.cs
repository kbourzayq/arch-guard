using ArchitectureGuardian.Core.Baselines;

namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Classifies candidate diagnostic IDs without duplicating their evidence payload.</summary>
/// <param name="BaseCommit">Resolved full base commit identity.</param>
/// <param name="Existing">Candidate diagnostic IDs uniquely matched to the base.</param>
/// <param name="New">Unmatched or ambiguous candidate diagnostic IDs used by the gate.</param>
/// <param name="Resolved">Base fingerprints absent from a completed candidate.</param>
internal sealed record ChangeSummary(string BaseCommit, IReadOnlyList<string> Existing,
    IReadOnlyList<string> New, IReadOnlyList<BaselineEntry> Resolved);
