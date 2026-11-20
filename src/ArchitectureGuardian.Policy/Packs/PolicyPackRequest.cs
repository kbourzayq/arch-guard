namespace ArchitectureGuardian.Policy.Packs;

/// <summary>Identifies a pinned local rule pack that a host must read.</summary>
public sealed class PolicyPackRequest
{
    /// <summary>Creates a request for an already validated pack reference.</summary>
    /// <param name="id">The pack identity, such as <c>acme-standards</c>.</param>
    /// <param name="version">The exact pinned version, such as <c>1.0.0</c>.</param>
    /// <param name="relativePath">
    /// The pack directory relative to the root policy directory, using <c>/</c> separators and no <c>.</c>,
    /// <c>..</c> or empty segments. The manifest is <see cref="ManifestFileName"/> inside that directory.
    /// </param>
    /// <exception cref="ArgumentException">A value is blank.</exception>
    public PolicyPackRequest(string id, string version, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Pack identity, version and relative path are required.");
        }

        Id = id;
        Version = version;
        RelativePath = relativePath;
    }

    /// <summary>The manifest file name inside every pack directory.</summary>
    public const string ManifestFileName = "pack.yaml";

    /// <summary>Gets the pack identity.</summary>
    public string Id { get; }

    /// <summary>Gets the exact pinned version.</summary>
    public string Version { get; }

    /// <summary>Gets the pack directory relative to the root policy directory.</summary>
    public string RelativePath { get; }

    /// <summary>Gets the manifest path relative to the root policy directory.</summary>
    public string ManifestPath => RelativePath + "/" + ManifestFileName;
}
