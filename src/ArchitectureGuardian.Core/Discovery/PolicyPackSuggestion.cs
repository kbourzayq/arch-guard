namespace ArchitectureGuardian.Core.Discovery;

/// <summary>A built-in rule pack that a proposal suggests extending, with the declarations the solution does not use.</summary>
public sealed class PolicyPackSuggestion
{
    /// <summary>Snapshots a pack reference.</summary>
    /// <param name="id">The built-in pack identity, such as abp.</param>
    /// <param name="version">The exact pack version.</param>
    /// <param name="exclusions">Pack declarations to exclude, such as layer:HttpApiClient, because no project supplies them.</param>
    /// <exception cref="ArgumentException">The identity or version is blank, or an exclusion is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="exclusions"/> is null.</exception>
    public PolicyPackSuggestion(string id, string version, IEnumerable<string> exclusions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(exclusions);
        var values = exclusions.ToArray();
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Exclusions cannot be blank.", nameof(exclusions));
        }

        Id = id;
        Version = version;
        Exclusions = Array.AsReadOnly(values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Gets the built-in pack identity.</summary>
    public string Id { get; }
    /// <summary>Gets the exact pack version.</summary>
    public string Version { get; }
    /// <summary>Gets the pack declarations to exclude, in ordinal order.</summary>
    public IReadOnlyList<string> Exclusions { get; }
}
