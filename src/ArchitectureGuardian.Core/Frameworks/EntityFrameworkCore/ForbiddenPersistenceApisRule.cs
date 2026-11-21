using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.EntityFrameworkCore;

/// <summary>Checks EF003: source does not reference EF Core methods that the policy forbids.</summary>
/// <remarks>
/// A reference proves the policy is violated. IgnoreQueryFilters is reported as a potential concern because bypassing
/// query filters is evidence for review: whether tenant or soft-deleted data is exposed depends on runtime data and filters.
/// </remarks>
public sealed class ForbiddenPersistenceApisRule : IArchitectureRule<DependencyBuildResult>
{
    private const string IgnoreQueryFilters = "IgnoreQueryFilters";

    /// <inheritdoc/>
    public RuleDescriptor Descriptor => EntityFrameworkCoreRuleCatalog.ForbiddenApis;

    /// <summary>Reports each forbidden EF Core method that a type references.</summary>
    /// <param name="context">The repository, EF Core facts and upstream coverage.</param>
    /// <param name="cancellationToken">Cooperative cancellation during evaluation.</param>
    /// <returns>Classified findings with source evidence, or an incomplete result when facts are unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupts evaluation.</exception>
    public Task<RuleEvaluationResult> EvaluateAsync(DependencyBuildResult context, CancellationToken cancellationToken = default)
        => EntityFrameworkCoreEvaluation.Run(context, Descriptor, (repository, facts, violations, issues) =>
        {
            var forbidden = repository.ResolvedPolicy.Policy.EntityFrameworkCore?.ForbiddenApis ?? [];
            foreach (var group in facts.ApiCalls.Where(item => forbidden.Contains(item.Method, StringComparer.Ordinal))
                         .GroupBy(item => (item.Project, item.User, item.Method)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (project, user, method) = group.Key;
                if (EntityFrameworkCoreEvaluation.Mapping(repository, project, Descriptor.Id, issues) is not { } source)
                {
                    continue;
                }

                var filters = method == IgnoreQueryFilters;
                // A simple name can match several bound APIs, such as DbContext.Add and DbSet<T>.Add; each site keeps its own.
                var apis = string.Join("', '", group.Select(item => item.Symbol).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
                var violation = $"Type '{user}' in project '{project}' references forbidden EF Core API '{apis}'.";
                var message = filters
                    ? $"{violation} It bypasses global query filters; this is policy evidence for review and does not prove that tenant or other filtered data is exposed."
                    : violation;
                violations.Add(EntityFrameworkCoreEvaluation.Violation(Descriptor, source.ModuleId, project, user, message,
                    filters
                        ? "Keep query filters applied, or confirm with tests that this query cannot return another tenant's data and document the exception."
                        : $"Replace '{method}' with an API the policy allows, such as a parameterized LINQ query.",
                    group.Select(item => new DiagnosticEvidence($"Type '{user}' references forbidden EF Core API '{item.Symbol}'.", item.Location)),
                    filters ? ViolationClassification.Potential : ViolationClassification.Proven, "forbiddenApis",
                    // Only the matched entry is the violated constraint, so banning another API keeps existing fingerprints.
                    [method], method));
            }
        }, cancellationToken);
}
