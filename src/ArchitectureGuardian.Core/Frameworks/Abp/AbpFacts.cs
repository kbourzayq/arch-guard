namespace ArchitectureGuardian.Core.Frameworks.Abp;

/// <summary>Neutral ABP Framework facts extracted from compilations, independent of Roslyn and any host.</summary>
/// <remarks>Only statically visible source is represented; module loading, conventional controllers and permissions are runtime.</remarks>
public sealed class AbpFacts
{
    /// <summary>Snapshots the facts in deterministic order.</summary>
    /// <param name="modules">ABP module classes declared in solution source.</param>
    /// <param name="projectReferences">Direct references between solution projects.</param>
    /// <param name="entityExposures">Application service members whose signatures expose entities.</param>
    /// <param name="repositoryUses">ABP repository type uses.</param>
    /// <param name="serviceMethods">Public application service methods that ABP can expose remotely.</param>
    /// <param name="tenantBypasses">Calls that leave the current tenant's data isolation.</param>
    /// <exception cref="ArgumentNullException">A collection is null.</exception>
    /// <exception cref="ArgumentException">A collection contains null.</exception>
    public AbpFacts(IEnumerable<AbpModuleClass> modules, IEnumerable<AbpProjectReference> projectReferences,
        IEnumerable<AbpEntityExposure> entityExposures, IEnumerable<AbpRepositoryUse> repositoryUses,
        IEnumerable<AbpServiceMethod> serviceMethods, IEnumerable<AbpTenantBypass> tenantBypasses)
    {
        Modules = Snapshot(modules, nameof(modules), item => (item.Project, item.Module, item.Location?.CanonicalKey));
        ProjectReferences = Snapshot(projectReferences, nameof(projectReferences), item => (item.Project, item.ReferencedProject, (string?)null));
        EntityExposures = Snapshot(entityExposures, nameof(entityExposures), item => (item.Project, item.Service + "\0" + item.Entity + "\0" + item.Member, item.Location?.CanonicalKey));
        RepositoryUses = Snapshot(repositoryUses, nameof(repositoryUses), item => (item.Project, item.User + "\0" + item.Repository, item.Location?.CanonicalKey));
        ServiceMethods = Snapshot(serviceMethods, nameof(serviceMethods), item => (item.Project, item.Service + "\0" + item.Method, item.Location?.CanonicalKey));
        TenantBypasses = Snapshot(tenantBypasses, nameof(tenantBypasses), item => (item.Project, item.User + "\0" + item.Api, item.Location?.CanonicalKey));
    }

    /// <summary>Gets the module classes in project and module order.</summary>
    public IReadOnlyList<AbpModuleClass> Modules { get; }
    /// <summary>Gets the direct project references in ordinal order.</summary>
    public IReadOnlyList<AbpProjectReference> ProjectReferences { get; }
    /// <summary>Gets the entity exposures in project, service and entity order.</summary>
    public IReadOnlyList<AbpEntityExposure> EntityExposures { get; }
    /// <summary>Gets the repository uses in project, user and repository order.</summary>
    public IReadOnlyList<AbpRepositoryUse> RepositoryUses { get; }
    /// <summary>Gets the remotely exposable service methods in project, service and method order.</summary>
    public IReadOnlyList<AbpServiceMethod> ServiceMethods { get; }
    /// <summary>Gets the tenant-isolation bypasses in project, user and API order.</summary>
    public IReadOnlyList<AbpTenantBypass> TenantBypasses { get; }

    private static IReadOnlyList<T> Snapshot<T>(IEnumerable<T> values, string name, Func<T, (string, string, string?)> key)
    {
        ArgumentNullException.ThrowIfNull(values, name);
        var copy = values.ToArray();
        if (copy.Any(item => item is null))
        {
            throw new ArgumentException("Facts cannot contain null.", name);
        }

        return Array.AsReadOnly(copy.OrderBy(item => key(item).Item1, StringComparer.Ordinal).ThenBy(item => key(item).Item2, StringComparer.Ordinal)
            .ThenBy(item => key(item).Item3, StringComparer.Ordinal).ToArray());
    }
}
