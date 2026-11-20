using ArchitectureGuardian.Policy.Packs;

namespace ArchitectureGuardian.Configuration.Yaml;

/// <summary>Reads local rule-pack manifests confined to the policy directory.</summary>
/// <remarks>
/// Local packs are repository content reviewed like the policy itself. The resolver rejects symbolic links and
/// reparse points so a pack cannot redirect reads outside the policy directory, and bounds manifest size.
/// Reads are synchronous because the portable composition port is shared with compiler hosts; manifests are small.
/// </remarks>
internal sealed class FilePolicyPackResolver : IPolicyPackResolver
{
    private const long MaximumManifestBytes = 262_144;

    private readonly string _policyDirectory;

    /// <summary>Creates a resolver for packs below one policy directory.</summary>
    /// <param name="policyDirectory">The directory containing the root policy.</param>
    internal FilePolicyPackResolver(string policyDirectory) => _policyDirectory = Path.GetFullPath(policyDirectory);

    /// <summary>Reads <c>&lt;relative path&gt;/pack.yaml</c> below the policy directory.</summary>
    /// <param name="request">The validated pack request.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>The manifest, or null when the directory or manifest does not exist.</returns>
    /// <exception cref="ArgumentException">The path traverses a link or the manifest is too large.</exception>
    /// <exception cref="IOException">The manifest cannot be read.</exception>
    public PolicyPackSource? Resolve(PolicyPackRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var current = _policyDirectory;
        foreach (var segment in request.ManifestPath.Split('/'))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                return null;
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException($"'{request.ManifestPath}' traverses a symbolic link or reparse point.");
            }
        }

        if (!File.Exists(current))
        {
            return null;
        }

        if (new FileInfo(current).Length > MaximumManifestBytes)
        {
            throw new ArgumentException($"'{request.ManifestPath}' exceeds {MaximumManifestBytes} bytes.");
        }

        return new PolicyPackSource(current, File.ReadAllText(current));
    }
}
