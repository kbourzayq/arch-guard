namespace ArchitectureGuardian.Policy.Composition;

/// <summary>A validated <c>extends</c> entry that pins one rule pack by identity and exact version.</summary>
internal sealed class PolicyPackReference
{
    /// <summary>Creates a pack reference.</summary>
    /// <param name="id">The pack identity.</param>
    /// <param name="version">The exact pinned version.</param>
    /// <param name="relativePath">The local pack directory relative to the root policy, or null for a built-in pack.</param>
    /// <param name="exclusions">Declarations or rules removed from the pack's contribution, with their positions.</param>
    /// <param name="origin">The <c>extends</c> entry.</param>
    internal PolicyPackReference(string id, string version, string? relativePath,
        IReadOnlyList<KeyValuePair<string, PolicyOrigin>> exclusions, PolicyOrigin origin)
    {
        Id = id;
        Version = version;
        RelativePath = relativePath;
        Exclusions = exclusions;
        Origin = origin;
    }

    /// <summary>Gets the pack identity.</summary>
    internal string Id { get; }

    /// <summary>Gets the exact pinned version.</summary>
    internal string Version { get; }

    /// <summary>Gets the local pack directory relative to the root policy, or null for a built-in pack.</summary>
    internal string? RelativePath { get; }

    /// <summary>Gets exclusion targets such as <c>layer:Api</c>, in document order.</summary>
    internal IReadOnlyList<KeyValuePair<string, PolicyOrigin>> Exclusions { get; }

    /// <summary>Gets the referencing <c>extends</c> entry.</summary>
    internal PolicyOrigin Origin { get; }

    /// <summary>Gets a label such as <c>clean-architecture@1.0.0</c>.</summary>
    internal string Label => Id + "@" + Version;

    /// <summary>Gets the source-qualified identity used to detect inconsistent references to one pack.</summary>
    internal string Identity => Label + (RelativePath is null ? " (built-in)" : " (local: " + RelativePath + ")");
}
