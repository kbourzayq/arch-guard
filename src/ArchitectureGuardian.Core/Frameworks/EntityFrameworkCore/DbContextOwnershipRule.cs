using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.EntityFrameworkCore;

/// <summary>Checks EF001: a DbContext is used only by the module that owns its declaring project.</summary>
/// <remarks>
/// Cross-module uses of a source DbContext are proven. Uses of the DbContext base type cannot show which module's
/// context is supplied, so they require runtime validation. Access through another module's repository contract
/// involves no DbContext and passes.
/// </remarks>
public sealed class DbContextOwnershipRule : IArchitectureRule<DependencyBuildResult>
{
    /// <inheritdoc/>
    public RuleDescriptor Descriptor => EntityFrameworkCoreRuleCatalog.DbContextOwnership;

    /// <summary>Reports each type that uses a DbContext owned by another module, or the undetermined DbContext base type.</summary>
    /// <param name="context">The repository, EF Core facts and upstream coverage.</param>
    /// <param name="cancellationToken">Cooperative cancellation during evaluation.</param>
    /// <returns>Classified findings with source evidence, or an incomplete result when facts are unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupts evaluation.</exception>
    public Task<RuleEvaluationResult> EvaluateAsync(DependencyBuildResult context, CancellationToken cancellationToken = default)
        => EntityFrameworkCoreEvaluation.Run(context, Descriptor, (repository, facts, violations, issues) =>
        {
            foreach (var use in facts.TypeUses.Where(item => item.IsOwnerAmbiguous))
            {
                // Guessing an owner could prove a false cross-module violation, so coverage is reduced explicitly instead.
                issues.Add(new("efcore-dbcontext-owner-ambiguous",
                    $"DbContext '{use.Symbol}' is declared in an assembly that several projects produce, so its owning module cannot be resolved.",
                    Descriptor.Id, use.Location));
            }

            // The declaring project is part of the key: equally named contexts from different assemblies have different owners.
            var groups = facts.TypeUses.Where(item => item.Kind == EntityFrameworkCoreTypeKind.BaseDbContext
                    || item.Kind == EntityFrameworkCoreTypeKind.DbContext && item.DeclaringProject is not null)
                .GroupBy(item => (item.Project, item.User, item.Symbol, item.Kind, item.DeclaringProject));
            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (project, user, symbol, kind, declaring) = group.Key;
                if (EntityFrameworkCoreEvaluation.Mapping(repository, project, Descriptor.Id, issues) is not { } source)
                {
                    continue;
                }

                if (kind == EntityFrameworkCoreTypeKind.BaseDbContext)
                {
                    var message = $"Type '{user}' in project '{project}' uses the DbContext base type, so the module that owns the supplied context is decided at runtime.";
                    violations.Add(EntityFrameworkCoreEvaluation.Violation(Descriptor, source.ModuleId, project, user, message,
                        $"Depend on the owning module's DbContext subclass, or confirm in tests that '{user}' receives only a context owned by module '{source.ModuleId}'.",
                        group.Select(item => new DiagnosticEvidence(message, item.Location)), ViolationClassification.RuntimeValidationRequired,
                        "dbContextOwner", [source.ModuleId], symbol));
                    continue;
                }

                if (EntityFrameworkCoreEvaluation.Mapping(repository, declaring!, Descriptor.Id, issues) is not { } owner || owner.ModuleId == source.ModuleId)
                {
                    continue;
                }

                var violation = $"Type '{user}' in module '{source.ModuleId}' uses DbContext '{symbol}', which module '{owner.ModuleId}' owns.";
                violations.Add(EntityFrameworkCoreEvaluation.Violation(Descriptor, source.ModuleId, project, user,
                    $"{violation} Project '{project}' must not use another module's DbContext.",
                    $"Use a repository or service contract published by module '{owner.ModuleId}', or move this data access into module '{owner.ModuleId}'.",
                    group.Select(item => new DiagnosticEvidence(violation, item.Location)), ViolationClassification.Proven,
                    "dbContextOwner", [owner.ModuleId], symbol));
            }
        }, cancellationToken);
}
