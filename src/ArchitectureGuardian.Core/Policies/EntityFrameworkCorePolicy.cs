#if PORTABLE_POLICY
namespace ArchitectureGuardian.Policy.Documents;
#else
namespace ArchitectureGuardian.Core.Policies;
#endif

/// <summary>Declares the EF Core persistence boundary used by EF002 and the persistence APIs forbidden by EF003.</summary>
/// <remarks>
/// EF001 needs no settings: a DbContext is owned by the module of the project that declares it. The settings here
/// are references to declared layers and EF Core member names; they never select a rule on their own.
/// </remarks>
public sealed class EntityFrameworkCorePolicy
{
    /// <summary>Snapshots the persistence layers and forbidden API names.</summary>
    /// <param name="persistenceLayers">Layer IDs whose projects may use EF Core types, or null for none.</param>
    /// <param name="forbiddenApis">EF Core method names, such as IgnoreQueryFilters or FromSqlRaw, or null for none.</param>
    /// <exception cref="ArgumentException">A value is blank, duplicated, not a layer identity or not a C# identifier.</exception>
    public EntityFrameworkCorePolicy(IEnumerable<string>? persistenceLayers = null, IEnumerable<string>? forbiddenApis = null)
    {
        PersistenceLayers = PolicyData.Unique((persistenceLayers ?? []).Select(PolicyData.Id), value => value, "persistence layer");
        ForbiddenApis = PolicyData.Strings(forbiddenApis ?? []);
        if (ForbiddenApis.Any(name => !IsIdentifier(name)))
        {
            throw new ArgumentException("forbiddenApis requires EF Core method names such as IgnoreQueryFilters; type names, signatures and wildcards are unsupported.");
        }
    }

    /// <summary>Gets the layer IDs whose projects may use EF Core types, in ordinal order.</summary>
    public IReadOnlyList<string> PersistenceLayers { get; }

    /// <summary>Gets the forbidden EF Core method names, in ordinal order.</summary>
    public IReadOnlyList<string> ForbiddenApis { get; }

    /// <summary>Checks references against declared layers and settings required by the selected EF rules.</summary>
    /// <param name="policy">The EF Core settings, or null when the policy declares none.</param>
    /// <param name="layerIds">The declared layer identities.</param>
    /// <param name="selectedRules">The selected rule IDs.</param>
    /// <exception cref="ArgumentException">A persistence layer is undeclared, or a selected rule lacks its required setting.</exception>
    internal static void Validate(EntityFrameworkCorePolicy? policy, IEnumerable<string> layerIds, IEnumerable<string> selectedRules)
    {
        var layers = new HashSet<string>(layerIds, StringComparer.Ordinal);
        var unknown = policy?.PersistenceLayers.FirstOrDefault(layer => !layers.Contains(layer));
        if (unknown is not null)
        {
            throw new ArgumentException($"entityFrameworkCore.persistenceLayers: Unknown layer '{unknown}'; declare the layer first.");
        }

        var rules = new HashSet<string>(selectedRules, StringComparer.Ordinal);
        // A selected rule without its setting would otherwise report every use or none, neither of which the user declared.
        if (rules.Contains("EF002") && (policy is null || policy.PersistenceLayers.Count == 0))
        {
            throw new ArgumentException("entityFrameworkCore.persistenceLayers: EF002 is selected; declare at least one layer whose projects may use EF Core.");
        }

        if (rules.Contains("EF003") && (policy is null || policy.ForbiddenApis.Count == 0))
        {
            throw new ArgumentException("entityFrameworkCore.forbiddenApis: EF003 is selected; declare at least one forbidden EF Core method name.");
        }
    }

    private static bool IsIdentifier(string name) => (char.IsLetter(name[0]) || name[0] == '_')
        && name.All(character => char.IsLetterOrDigit(character) || character == '_');
}
