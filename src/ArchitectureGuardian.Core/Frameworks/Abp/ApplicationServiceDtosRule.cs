using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.Abp;

/// <summary>Checks ABP002: application services exchange DTOs, never entities, across their public signatures.</summary>
/// <remarks>An entity in a signature is proven from the declaration; it couples clients to the domain model and can leak data.</remarks>
public sealed class ApplicationServiceDtosRule : IArchitectureRule<DependencyBuildResult>
{
    /// <inheritdoc/>
    public RuleDescriptor Descriptor => AbpRuleCatalog.ApplicationServiceDtos;

    /// <summary>Reports each entity that an application service exposes, with every exposing member as evidence.</summary>
    /// <param name="context">The repository, ABP facts and upstream coverage.</param>
    /// <param name="cancellationToken">Cooperative cancellation during evaluation.</param>
    /// <returns>Proven findings with member evidence, or an incomplete result when facts are unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupts evaluation.</exception>
    public Task<RuleEvaluationResult> EvaluateAsync(DependencyBuildResult context, CancellationToken cancellationToken = default)
        => AbpEvaluation.Run(context, Descriptor, (repository, facts, violations, issues) =>
        {
            foreach (var group in facts.EntityExposures.GroupBy(item => (item.Project, item.Service, item.Entity)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (project, service, entity) = group.Key;
                if (AbpEvaluation.Mapping(repository, project, Descriptor.Id, issues) is not { } source)
                {
                    continue;
                }

                var members = string.Join(", ", group.Select(item => item.Member).Distinct(StringComparer.Ordinal));
                var violation = $"Application service '{service}' exposes entity '{entity}' through {members}.";
                violations.Add(AbpEvaluation.Violation(Descriptor, source.ModuleId, project, service,
                    $"{violation} Application services must exchange DTOs so clients do not depend on, or receive, the domain model.",
                    $"Return or accept a DTO for '{entity}', declared in the Application.Contracts layer, and map it in the application service.",
                    group.Select(item => new DiagnosticEvidence($"'{item.Member}' exposes entity '{entity}'.", item.Location)),
                    ViolationClassification.Proven, "dtoBoundary", [entity], entity));
            }
        }, cancellationToken);
}
