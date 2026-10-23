using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Baselines;

/// <summary>Classifies findings without hiding available diagnostics or operational coverage failures.</summary>
public sealed class BaselineComparison
{
    /// <summary>Matches only unique exact semantic fingerprints; ambiguous candidates remain new.</summary>
    /// <param name="analysis">Current analysis, including partial results and coverage status.</param>
    /// <param name="baseline">The explicitly supplied accepted inventory.</param>
    public BaselineComparison(AnalysisResult analysis, BaselineSnapshot baseline)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(baseline);
        Analysis = analysis;
        var current = analysis.Diagnostics.Select(diagnostic => (Diagnostic: diagnostic, Entry: BaselineEntry.FromDiagnostic(diagnostic))).ToArray();
        var accepted = baseline.Entries.GroupBy(entry => entry.Fingerprint, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var counts = current.GroupBy(item => item.Entry.Fingerprint, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var matched = counts.Where(pair => pair.Value == 1 && accepted.GetValueOrDefault(pair.Key) == 1)
            .Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        Existing = Array.AsReadOnly(current.Where(item => matched.Contains(item.Entry.Fingerprint)).Select(item => item.Diagnostic).ToArray());
        New = Array.AsReadOnly(current.Where(item => !matched.Contains(item.Entry.Fingerprint)).Select(item => item.Diagnostic).ToArray());
        // Missing findings cannot be called resolved until coverage is complete.
        Resolved = Array.AsReadOnly(analysis.IsComplete
            ? baseline.Entries.Where(entry => !counts.ContainsKey(entry.Fingerprint)).ToArray() : []);
    }

    /// <summary>Gets the unmodified analysis with all diagnostics and operational issues.</summary>
    public AnalysisResult Analysis { get; }
    /// <summary>Gets current findings uniquely matched to accepted debt.</summary>
    public IReadOnlyList<Diagnostic> Existing { get; }
    /// <summary>Gets unmatched and ambiguous current findings, subject to the severity gate.</summary>
    public IReadOnlyList<Diagnostic> New { get; }
    /// <summary>Gets baseline entries absent from complete analysis; ambiguous matches are not resolutions.</summary>
    public IReadOnlyList<BaselineEntry> Resolved { get; }
}
