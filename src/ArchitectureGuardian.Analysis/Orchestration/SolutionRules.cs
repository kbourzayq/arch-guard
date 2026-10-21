using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Analysis.Orchestration;

/// <summary>Registers the solution engine's available rules for CLI execution and protocol catalog discovery.</summary>
public static class SolutionRules
{
    /// <summary>Gets the immutable implementation inventory; policy selection remains explicit and independent.</summary>
    public static IReadOnlyList<IArchitectureRule<DependencyBuildResult>> All { get; } =
        Array.AsReadOnly<IArchitectureRule<DependencyBuildResult>>([new ModuleDependenciesRule(), new LayerDependenciesRule(),
            new ModuleCyclesRule(), new ImplementationBoundariesRule(), new ForbiddenReferencesRule(), new DomainInfrastructureRule()]);
}
