using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Configuration.Validation;

/// <summary>Reports an invalid policy with a document location and a logical configuration path.</summary>
public sealed class PolicyConfigurationException : Exception
{
    /// <summary>Creates an actionable configuration error.</summary>
    /// <param name="message">The validation problem and corrective guidance.</param>
    /// <param name="policyPath">The logical YAML field path, starting at $.</param>
    /// <param name="location">The one-based document position.</param>
    /// <param name="innerException">The original parser or contract failure, when available.</param>
    public PolicyConfigurationException(string message, string policyPath, SourceLocation location, Exception? innerException = null)
        : base($"{location.Path}({location.StartLine},{location.StartColumn}) {policyPath}: {message}", innerException)
    {
        PolicyPath = policyPath;
        Location = location;
    }

    /// <summary>Gets the logical field path containing the invalid configuration.</summary>
    public string PolicyPath { get; }

    /// <summary>Gets the document location at which validation failed.</summary>
    public SourceLocation Location { get; }
}
