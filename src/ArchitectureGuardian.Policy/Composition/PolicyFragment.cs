using ArchitectureGuardian.Policy.Documents;

namespace ArchitectureGuardian.Policy.Composition;

/// <summary>The validated, not yet composed contents of one policy document or rule-pack manifest.</summary>
internal sealed class PolicyFragment
{
    /// <summary>Creates an empty fragment for one document.</summary>
    /// <param name="root">The document root origin.</param>
    internal PolicyFragment(PolicyOrigin root) => Root = root;

    /// <summary>Gets the document root origin.</summary>
    internal PolicyOrigin Root { get; }

    /// <summary>Gets the settings this document declares directly.</summary>
    internal Dictionary<PolicySlotKey, PolicySlot> Slots { get; } = new();

    /// <summary>Gets the packs this document extends, in document order.</summary>
    internal List<PolicyPackReference> Extends { get; } = [];

    /// <summary>Gets the <c>extends</c> field origin, when present.</summary>
    internal PolicyOrigin? ExtendsOrigin { get; set; }

    /// <summary>Gets the identity a pack manifest declares; null for a root policy.</summary>
    internal string? PackId { get; set; }

    /// <summary>Gets the version a pack manifest declares; null for a root policy.</summary>
    internal string? PackVersion { get; set; }

    /// <summary>Gets declaration positions keyed by logical field path in this document.</summary>
    internal Dictionary<string, PolicyPosition> Locations { get; } = new(StringComparer.Ordinal);
}
