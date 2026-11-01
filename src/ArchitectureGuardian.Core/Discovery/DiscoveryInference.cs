namespace ArchitectureGuardian.Core.Discovery;

/// <summary>A reviewable architectural suggestion, separate from verified observations.</summary>
public sealed class DiscoveryInference
{
    /// <summary>Snapshots a suggestion and its supporting observed signals.</summary>
    /// <param name="kind">The suggestion category.</param>
    /// <param name="subject">The suggested identity or affected project.</param>
    /// <param name="confidence">A qualitative confidence classification, never approval.</param>
    /// <param name="evidence">Observed paths, names or declarations supporting the suggestion.</param>
    public DiscoveryInference(string kind, string subject, string confidence, IEnumerable<string> evidence)
    {
        Kind = kind;
        Subject = subject;
        Confidence = confidence;
        Evidence = Array.AsReadOnly(evidence.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Gets the inference category.</summary>
    public string Kind { get; }
    /// <summary>Gets the suggested identity or affected project.</summary>
    public string Subject { get; }
    /// <summary>Gets high, medium or low confidence; all suggestions require review.</summary>
    public string Confidence { get; }
    /// <summary>Gets supporting observations in ordinal order.</summary>
    public IReadOnlyList<string> Evidence { get; }
}
