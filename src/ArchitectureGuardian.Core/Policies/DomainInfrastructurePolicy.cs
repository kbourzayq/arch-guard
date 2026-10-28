#if PORTABLE_POLICY
namespace ArchitectureGuardian.Policy.Documents;
#else
namespace ArchitectureGuardian.Core.Policies;
#endif

/// <summary>Declares AG006 technology restrictions and namespace exceptions for one source layer.</summary>
public sealed class DomainInfrastructurePolicy
{
    /// <summary>Snapshots explicit technology identities; no framework or layer names are built in.</summary>
    /// <param name="forbiddenAssemblies">Exact assembly simple names, or null for none.</param>
    /// <param name="forbiddenNamespaces">Namespace roots including descendants, or null for none.</param>
    /// <param name="allowedNamespaces">Target namespace roots exempt from these AG006 restrictions in this layer only.</param>
    /// <exception cref="ArgumentException">Names are invalid or duplicated.</exception>
    public DomainInfrastructurePolicy(IEnumerable<string>? forbiddenAssemblies = null,
        IEnumerable<string>? forbiddenNamespaces = null, IEnumerable<string>? allowedNamespaces = null)
    {
        ForbiddenAssemblies = PolicyData.ReferenceNames(forbiddenAssemblies ?? [], assembly: true);
        ForbiddenNamespaces = PolicyData.ReferenceNames(forbiddenNamespaces ?? [], assembly: false);
        AllowedNamespaces = PolicyData.ReferenceNames(allowedNamespaces ?? [], assembly: false);
    }

    /// <summary>Gets exact, case-sensitive technology assembly simple names.</summary>
    public IReadOnlyList<string> ForbiddenAssemblies { get; }
    /// <summary>Gets forbidden technology namespace roots, including descendants.</summary>
    public IReadOnlyList<string> ForbiddenNamespaces { get; }
    /// <summary>Gets target namespace exceptions, including descendants, scoped to the declaring layer's AG006 checks.</summary>
    public IReadOnlyList<string> AllowedNamespaces { get; }
}
