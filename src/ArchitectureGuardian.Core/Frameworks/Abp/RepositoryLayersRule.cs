using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.Abp;

/// <summary>Checks ABP003: ABP repositories are used only in projects of the declared repository layers.</summary>
/// <remarks>
/// A host, UI or HTTP API that reads repositories directly skips the application layer's authorization, validation,
/// unit of work and DTO mapping. Findings are proven from bound symbols.
/// </remarks>
public sealed class RepositoryLayersRule : IArchitectureRule<DependencyBuildResult>
{
    /// <inheritdoc/>
    public RuleDescriptor Descriptor => AbpRuleCatalog.RepositoryLayers;

    /// <summary>Reports each repository type used by a type outside the repository layers.</summary>
    /// <param name="context">The repository, ABP facts and upstream coverage.</param>
    /// <param name="cancellationToken">Cooperative cancellation during evaluation.</param>
    /// <returns>Proven findings with source evidence, or an incomplete result when facts are unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupts evaluation.</exception>
    public Task<RuleEvaluationResult> EvaluateAsync(DependencyBuildResult context, CancellationToken cancellationToken = default)
        => AbpEvaluation.Run(context, Descriptor, (repository, facts, violations, issues) =>
        {
            var layers = repository.ResolvedPolicy.Policy.Abp?.RepositoryLayers ?? [];
            foreach (var group in facts.RepositoryUses.GroupBy(item => (item.Project, item.User, item.Repository)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (project, user, symbol) = group.Key;
                if (AbpEvaluation.Mapping(repository, project, Descriptor.Id, issues) is not { } source
                    || layers.Contains(source.LayerId, StringComparer.Ordinal))
                {
                    continue;
                }

                var violation = $"Type '{user}' in layer '{source.LayerId}' uses repository '{symbol}'.";
                violations.Add(AbpEvaluation.Violation(Descriptor, source.ModuleId, project, user,
                    $"{violation} Project '{project}' is outside the repository layers ({string.Join(", ", layers)}), so the access bypasses the application layer.",
                    "Call an application service contract instead, or add the layer to abp.repositoryLayers if it is meant to own data access.",
                    group.Select(item => new DiagnosticEvidence(violation, item.Location)), ViolationClassification.Proven,
                    "repositoryLayers", layers, symbol));
            }
        }, cancellationToken);
}
