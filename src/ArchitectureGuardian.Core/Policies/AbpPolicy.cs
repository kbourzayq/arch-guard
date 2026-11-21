#if PORTABLE_POLICY
namespace ArchitectureGuardian.Policy.Documents;
#else
namespace ArchitectureGuardian.Core.Policies;
#endif

/// <summary>Declares the ABP Framework layers that may use repositories (ABP003) or bypass tenant isolation (ABP005).</summary>
/// <remarks>
/// ABP001, ABP002 and ABP004 need no settings. The settings here are references to declared layers; they never select a
/// rule on their own, and each list composes independently in rule packs.
/// </remarks>
public sealed class AbpPolicy
{
    /// <summary>Snapshots the repository and tenant-bypass layers.</summary>
    /// <param name="repositoryLayers">Layer IDs whose projects may use ABP repositories, or null for none.</param>
    /// <param name="tenantFilterBypassLayers">Layer IDs whose projects may leave tenant isolation, or null for none.</param>
    /// <exception cref="ArgumentException">A value is blank, duplicated or not a layer identity.</exception>
    public AbpPolicy(IEnumerable<string>? repositoryLayers = null, IEnumerable<string>? tenantFilterBypassLayers = null)
    {
        RepositoryLayers = PolicyData.Unique((repositoryLayers ?? []).Select(PolicyData.Id), value => value, "repository layer");
        TenantFilterBypassLayers = PolicyData.Unique((tenantFilterBypassLayers ?? []).Select(PolicyData.Id), value => value, "tenant filter bypass layer");
    }

    /// <summary>Gets the layer IDs whose projects may use ABP repositories, in ordinal order.</summary>
    public IReadOnlyList<string> RepositoryLayers { get; }

    /// <summary>Gets the layer IDs whose projects may disable the multi-tenant filter or switch to the host, in ordinal order.</summary>
    public IReadOnlyList<string> TenantFilterBypassLayers { get; }

    /// <summary>Checks references against declared layers and the setting required by ABP003.</summary>
    /// <param name="policy">The ABP settings, or null when the policy declares none.</param>
    /// <param name="layerIds">The declared layer identities.</param>
    /// <param name="selectedRules">The selected rule IDs.</param>
    /// <exception cref="ArgumentException">A layer is undeclared, or ABP003 is selected without repository layers.</exception>
    internal static void Validate(AbpPolicy? policy, IEnumerable<string> layerIds, IEnumerable<string> selectedRules)
    {
        var layers = new HashSet<string>(layerIds, StringComparer.Ordinal);
        var unknown = policy?.RepositoryLayers.FirstOrDefault(layer => !layers.Contains(layer));
        if (unknown is not null)
        {
            throw new ArgumentException($"abp.repositoryLayers: Unknown layer '{unknown}'; declare the layer first.");
        }

        unknown = policy?.TenantFilterBypassLayers.FirstOrDefault(layer => !layers.Contains(layer));
        if (unknown is not null)
        {
            throw new ArgumentException($"abp.tenantFilterBypassLayers: Unknown layer '{unknown}'; declare the layer first.");
        }

        // Without repository layers ABP003 would report every repository use, which no policy author intends.
        if (selectedRules.Contains("ABP003", StringComparer.Ordinal) && (policy is null || policy.RepositoryLayers.Count == 0))
        {
            throw new ArgumentException("abp.repositoryLayers: ABP003 is selected; declare at least one layer whose projects may use ABP repositories.");
        }
    }
}
