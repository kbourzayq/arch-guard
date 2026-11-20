using System.Reflection;
using System.Text;

namespace ArchitectureGuardian.Policy.Packs;

/// <summary>Publishes the versioned rule packs embedded in this assembly, available to both analysis engines.</summary>
/// <remarks>
/// Every released version of a built-in pack remains embedded under its exact version, so a pinned reference
/// keeps its meaning when newer pack versions ship.
/// </remarks>
public static class BuiltInPolicyPacks
{
    private const string Prefix = "packs/";
    private const string Suffix = "/" + PolicyPackRequest.ManifestFileName;

    private static readonly Assembly Owner = typeof(BuiltInPolicyPacks).Assembly;

    /// <summary>Gets the embedded packs as <c>id@version</c> labels in ordinal order.</summary>
    public static IReadOnlyList<string> Available { get; } = Array.AsReadOnly([
        .. Owner.GetManifestResourceNames()
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) &&
                           name.EndsWith(Suffix, StringComparison.Ordinal))
            .Select(name => name.Substring(Prefix.Length, name.Length - Prefix.Length - Suffix.Length).Split('/'))
            .Where(parts => parts.Length == 2)
            .Select(parts => parts[0] + "@" + parts[1])
            .OrderBy(label => label, StringComparer.Ordinal)
    ]);

    /// <summary>Gets the error-message path of an embedded manifest.</summary>
    /// <param name="id">The pack identity.</param>
    /// <param name="version">The exact version.</param>
    /// <returns>A path such as <c>builtin:clean-architecture@1.0.0/pack.yaml</c>.</returns>
    internal static string SourcePath(string id, string version) => "builtin:" + id + "@" + version + Suffix;

    /// <summary>Gets the embedded versions of a pack in ordinal order.</summary>
    /// <param name="id">The pack identity.</param>
    /// <returns>The versions; empty for an unknown pack.</returns>
    internal static IReadOnlyList<string> Versions(string id) => Available
        .Where(label => label.StartsWith(id + "@", StringComparison.Ordinal))
        .Select(label => label.Substring(id.Length + 1))
        .ToArray();

    /// <summary>Reads an embedded manifest.</summary>
    /// <param name="id">The pack identity.</param>
    /// <param name="version">The exact version.</param>
    /// <returns>The manifest YAML, or null when that exact pack version is not embedded.</returns>
    internal static string? Read(string id, string version)
    {
        using var stream = Owner.GetManifestResourceStream(Prefix + id + "/" + version + Suffix);
        if (stream is null) { return null; }
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
