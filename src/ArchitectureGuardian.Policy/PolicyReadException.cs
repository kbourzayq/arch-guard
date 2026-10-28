using ArchitectureGuardian.Policy.Documents;

namespace ArchitectureGuardian.Policy;

/// <summary>Reports a portable configuration failure with its logical and physical document positions.</summary>
/// <param name="problem">The corrective guidance.</param>
/// <param name="policyPath">The logical field path.</param>
/// <param name="sourcePath">The document path.</param>
/// <param name="position">The document position.</param>
/// <param name="innerException">The underlying parser or contract exception.</param>
public sealed class PolicyReadException(string problem, string policyPath, string sourcePath,
    PolicyPosition position, Exception? innerException = null)
    : Exception($"{sourcePath}({position.Line},{position.Column}) {policyPath}: {problem}", innerException)
{
    /// <summary>Gets the corrective guidance without a location prefix.</summary>
    public string Problem { get; } = problem;
    /// <summary>Gets the logical field path.</summary>
    public string PolicyPath { get; } = policyPath;
    /// <summary>Gets the document path.</summary>
    public string SourcePath { get; } = sourcePath;
    /// <summary>Gets the document position.</summary>
    public PolicyPosition Position { get; } = position;
}
