using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.EntityFrameworkCore;

/// <summary>Checks EF002: EF Core types are used only in projects of the declared persistence layers.</summary>
/// <remarks>Findings are proven from bound symbols; repository interfaces and other non-EF abstractions pass.</remarks>
public sealed class PersistenceLayersRule : IArchitectureRule<DependencyBuildResult>
{
    /// <inheritdoc/>
    public RuleDescriptor Descriptor => EntityFrameworkCoreRuleCatalog.PersistenceLayers;

    /// <summary>Reports each EF Core type used by a type outside the persistence layers.</summary>
    /// <param name="context">The repository, EF Core facts and upstream coverage.</param>
    /// <param name="cancellationToken">Cooperative cancellation during evaluation.</param>
    /// <returns>Proven findings with source evidence, or an incomplete result when facts are unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupts evaluation.</exception>
    public Task<RuleEvaluationResult> EvaluateAsync(DependencyBuildResult context, CancellationToken cancellationToken = default)
        => EntityFrameworkCoreEvaluation.Run(context, Descriptor, (repository, facts, violations, issues) =>
        {
            var layers = repository.ResolvedPolicy.Policy.EntityFrameworkCore?.PersistenceLayers ?? [];
            foreach (var group in facts.TypeUses.GroupBy(item => (item.Project, item.User, item.Symbol)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (project, user, symbol) = group.Key;
                if (EntityFrameworkCoreEvaluation.Mapping(repository, project, Descriptor.Id, issues) is not { } source
                    || layers.Contains(source.LayerId, StringComparer.Ordinal))
                {
                    continue;
                }

                var violation = $"Type '{user}' in layer '{source.LayerId}' uses EF Core type '{symbol}'.";
                violations.Add(EntityFrameworkCoreEvaluation.Violation(Descriptor, source.ModuleId, project, user,
                    $"{violation} Project '{project}' is outside the persistence layers ({string.Join(", ", layers)}).",
                    "Move the data access into a persistence-layer repository and depend on its interface, or add the layer to entityFrameworkCore.persistenceLayers if it is meant to own persistence.",
                    group.Select(item => new DiagnosticEvidence(violation, item.Location)), ViolationClassification.Proven,
                    "persistenceLayers", layers, symbol));
            }
        }, cancellationToken);
}
