using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Canonicalization;

namespace ArchitectureGuardian.Core.Baselines;

/// <summary>An immutable, versioned inventory of explicitly accepted findings.</summary>
public sealed class BaselineSnapshot
{
    /// <summary>Gets the supported persistence and fingerprint schema version.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Snapshots entries in deterministic fingerprint order; duplicates remain visible as ambiguous matches.</summary>
    /// <param name="entries">Accepted findings; an empty inventory is valid.</param>
    public BaselineSnapshot(IEnumerable<BaselineEntry> entries) =>
        Entries = CanonicalData.Snapshot(entries, entry => entry.Fingerprint);

    /// <summary>Gets the immutable entries, including duplicates rather than silently collapsing ambiguity.</summary>
    public IReadOnlyList<BaselineEntry> Entries { get; }

    /// <summary>Captures a complete analysis for an explicit baseline update.</summary>
    /// <param name="result">The complete result, regardless of its severity gate outcome.</param>
    /// <exception cref="InvalidOperationException">Analysis is incomplete, failed or cancelled.</exception>
    public static BaselineSnapshot Capture(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.IsComplete)
        {
            throw new InvalidOperationException("Cannot update a baseline without completed analysis.");
        }

        return new(result.Diagnostics.Select(BaselineEntry.FromDiagnostic));
    }
}
