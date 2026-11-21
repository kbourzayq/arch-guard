namespace ArchitectureGuardian.Core.Frameworks.EntityFrameworkCore;

/// <summary>Neutral EF Core facts extracted from compilations, independent of Roslyn and any host.</summary>
/// <remarks>Only statically visible source is represented; runtime composition and query translation are out of scope.</remarks>
public sealed class EntityFrameworkCoreFacts
{
    /// <summary>Snapshots the type uses and method references in deterministic order.</summary>
    /// <param name="typeUses">EF Core type uses found in solution source.</param>
    /// <param name="apiCalls">EF Core method references found in solution source.</param>
    /// <exception cref="ArgumentNullException">A collection is null.</exception>
    /// <exception cref="ArgumentException">A collection contains null.</exception>
    public EntityFrameworkCoreFacts(IEnumerable<EntityFrameworkCoreTypeUse> typeUses, IEnumerable<EntityFrameworkCoreApiCall> apiCalls)
    {
        ArgumentNullException.ThrowIfNull(typeUses);
        ArgumentNullException.ThrowIfNull(apiCalls);
        var uses = typeUses.ToArray();
        var calls = apiCalls.ToArray();
        if (uses.Any(item => item is null) || calls.Any(item => item is null))
        {
            throw new ArgumentException("Facts cannot contain null.");
        }

        TypeUses = Array.AsReadOnly(uses
            .OrderBy(item => item.Project, StringComparer.Ordinal).ThenBy(item => item.User, StringComparer.Ordinal)
            .ThenBy(item => item.Symbol, StringComparer.Ordinal).ThenBy(item => item.Location?.CanonicalKey, StringComparer.Ordinal).ToArray());
        ApiCalls = Array.AsReadOnly(calls
            .OrderBy(item => item.Project, StringComparer.Ordinal).ThenBy(item => item.User, StringComparer.Ordinal)
            .ThenBy(item => item.Symbol, StringComparer.Ordinal).ThenBy(item => item.Location?.CanonicalKey, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Gets the type uses in project, user and symbol order.</summary>
    public IReadOnlyList<EntityFrameworkCoreTypeUse> TypeUses { get; }
    /// <summary>Gets the method references in project, user and symbol order.</summary>
    public IReadOnlyList<EntityFrameworkCoreApiCall> ApiCalls { get; }
}
