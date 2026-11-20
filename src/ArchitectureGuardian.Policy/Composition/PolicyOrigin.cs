using ArchitectureGuardian.Policy.Documents;

namespace ArchitectureGuardian.Policy.Composition;

/// <summary>Records which document and field supplied a policy setting, so composed errors stay actionable.</summary>
internal sealed class PolicyOrigin
{
    /// <summary>Creates an origin for a declaration or value.</summary>
    /// <param name="sourcePath">The policy or pack manifest path shown in errors.</param>
    /// <param name="policyPath">The logical field path inside that document, starting at $.</param>
    /// <param name="position">The one-based document position.</param>
    /// <param name="document">A human-readable document label, such as "the policy" or "pack 'id@1.0.0'".</param>
    internal PolicyOrigin(string sourcePath, string policyPath, PolicyPosition position, string document)
    {
        SourcePath = sourcePath;
        PolicyPath = policyPath;
        Position = position;
        Document = document;
    }

    /// <summary>Gets the policy or pack manifest path.</summary>
    internal string SourcePath { get; }

    /// <summary>Gets the logical field path inside the source document.</summary>
    internal string PolicyPath { get; }

    /// <summary>Gets the one-based document position.</summary>
    internal PolicyPosition Position { get; }

    /// <summary>Gets the human-readable document label.</summary>
    internal string Document { get; }

    /// <summary>Creates an origin for a nested field in the same document.</summary>
    /// <param name="suffix">The field path appended to <see cref="PolicyPath"/>.</param>
    /// <returns>An origin sharing this document and position.</returns>
    internal PolicyOrigin Field(string suffix) => new(SourcePath, PolicyPath + suffix, Position, Document);

    /// <summary>Creates a positioned configuration error for this origin.</summary>
    /// <param name="problem">The corrective guidance.</param>
    /// <param name="innerException">The underlying failure, when available.</param>
    /// <returns>An exception for the caller to throw.</returns>
    internal PolicyReadException Error(string problem, Exception? innerException = null) =>
        new(problem, PolicyPath, SourcePath, Position, innerException);
}
