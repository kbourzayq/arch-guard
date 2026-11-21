using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Frameworks.Abp;
using ArchitectureGuardian.Core.Frameworks.AspNetCore;
using ArchitectureGuardian.Core.Frameworks.EntityFrameworkCore;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Analysis.Orchestration;

/// <summary>Registers the solution engine's available rules for CLI execution and protocol catalog discovery.</summary>
public static class SolutionRules
{
    /// <summary>Gets the immutable implementation inventory; policy selection remains explicit and independent.</summary>
    public static IReadOnlyList<IArchitectureRule<DependencyBuildResult>> All { get; } =
        Array.AsReadOnly<IArchitectureRule<DependencyBuildResult>>([new ModuleDependenciesRule(), new LayerDependenciesRule(),
            new ModuleCyclesRule(), new ImplementationBoundariesRule(), new ForbiddenReferencesRule(), new DomainInfrastructureRule(),
            new ControllerPersistenceRule(), new DeclaredAuthorizationRule(), new ServiceLifetimesRule(), new ApiPersistenceRule(),
            new ApplicationAbstractionsRule(), new DbContextOwnershipRule(), new PersistenceLayersRule(), new ForbiddenPersistenceApisRule(),
            new ModuleDependencyDeclarationsRule(), new ApplicationServiceDtosRule(), new RepositoryLayersRule(),
            new ApplicationServiceAuthorizationRule(), new TenantIsolationRule()]);
}
