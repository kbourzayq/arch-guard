namespace ArchitectureGuardian.Policy.Packs;

/// <summary>A rule-pack manifest supplied by a host.</summary>
public sealed class PolicyPackSource
{
    /// <summary>Creates a manifest source.</summary>
    /// <param name="sourcePath">The path shown in configuration errors.</param>
    /// <param name="content">The manifest YAML.</param>
    /// <exception cref="ArgumentException"><paramref name="sourcePath"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public PolicyPackSource(string sourcePath, string content)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) { throw new ArgumentException("Source path cannot be blank."); }
        SourcePath = sourcePath;
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>Gets the path shown in configuration errors.</summary>
    public string SourcePath { get; }

    /// <summary>Gets the manifest YAML.</summary>
    public string Content { get; }
}
