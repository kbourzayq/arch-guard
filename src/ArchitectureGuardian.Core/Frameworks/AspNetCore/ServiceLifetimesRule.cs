using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.AspNetCore;

/// <summary>Checks ASP003: a singleton must not capture a scoped service.</summary>
/// <remarks>
/// Statically registered implementations are followed through transient services. A factory-registered singleton
/// hides its dependencies, so it is reported as needing runtime validation instead of as a proven violation.
/// </remarks>
public sealed class ServiceLifetimesRule : IArchitectureRule<DependencyBuildResult>
{
    /// <inheritdoc/>
    public RuleDescriptor Descriptor => AspNetCoreRuleCatalog.ServiceLifetimes;

    /// <summary>Finds captive scoped dependencies of singleton registrations.</summary>
    /// <param name="context">The repository, ASP.NET Core facts and upstream coverage.</param>
    /// <param name="cancellationToken">Cooperative cancellation during evaluation.</param>
    /// <returns>Classified findings with registration evidence, or an incomplete result when facts are unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupts evaluation.</exception>
    public Task<RuleEvaluationResult> EvaluateAsync(DependencyBuildResult context, CancellationToken cancellationToken = default)
        => AspNetCoreEvaluation.Run(context, Descriptor, (repository, facts, violations, issues) =>
        {
            var byService = facts.Registrations.GroupBy(item => item.ServiceType, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            foreach (var singleton in facts.Registrations.Where(item => item.Lifetime == AspNetCoreServiceLifetime.Singleton))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (AspNetCoreEvaluation.Module(repository, singleton.Project, Descriptor.Id, issues) is not { } module)
                {
                    continue;
                }

                if (singleton.IsFactory)
                {
                    var text = $"Singleton '{singleton.ServiceType}' is created by a factory, so its dependencies cannot be checked statically.";
                    violations.Add(AspNetCoreEvaluation.Violation(Descriptor, module, singleton.Project, singleton.ServiceType, text,
                        "Enable ServiceProviderOptions.ValidateScopes and ValidateOnBuild in tests or development, and resolve scoped services through IServiceScopeFactory inside the singleton.",
                        [new DiagnosticEvidence(text, singleton.Location)], ViolationClassification.RuntimeValidationRequired,
                        "lifetime", ["Singleton"], singleton.ServiceType));
                    continue;
                }

                var ambiguous = singleton.HasAmbiguousConstructor;
                var visited = new HashSet<string>(StringComparer.Ordinal) { singleton.ServiceType };
                var queue = new Queue<(string Service, string Path)>(singleton.ImplementationDependencies
                    .Select(item => (item, singleton.ServiceType + " → " + item)));
                while (queue.Count > 0)
                {
                    var (service, path) = queue.Dequeue();
                    if (!visited.Add(service) || !byService.TryGetValue(service, out var targets))
                    {
                        continue;
                    }

                    var lifetimes = targets.Select(item => item.Lifetime).Distinct().ToArray();
                    // Registrations are matched solution-wide, but another project's registrations may belong to a
                    // different container, so they can indicate a capture without proving it.
                    var sameProject = targets.All(item => string.Equals(item.Project, singleton.Project, StringComparison.Ordinal));
                    if (lifetimes.Contains(AspNetCoreServiceLifetime.Scoped))
                    {
                        var scoped = targets.First(item => item.Lifetime == AspNetCoreServiceLifetime.Scoped);
                        var proven = lifetimes.Length == 1 && !ambiguous && sameProject;
                        var text = proven
                            ? $"Singleton '{singleton.ServiceType}' captures scoped service '{service}' ({path})."
                            : $"Singleton '{singleton.ServiceType}' may capture scoped service '{service}' ({path}); the constructor choice, the service's registrations or their container are ambiguous.";
                        violations.Add(AspNetCoreEvaluation.Violation(Descriptor, module, singleton.Project, singleton.ServiceType, text,
                            "Register the singleton as scoped, make the dependency a singleton, or resolve it from an IServiceScopeFactory scope when needed.",
                            [new DiagnosticEvidence(text, singleton.Location), new DiagnosticEvidence($"'{service}' is registered as Scoped.", scoped.Location)],
                            proven ? ViolationClassification.Proven : ViolationClassification.Potential,
                            "lifetime", ["Singleton", "Scoped"], service));
                        continue;
                    }

                    if (lifetimes.All(item => item == AspNetCoreServiceLifetime.Transient))
                    {
                        // A transient inherits the lifetime of its consumer, so its own scoped dependencies are captured too.
                        ambiguous |= !sameProject || targets.Any(item => item.HasAmbiguousConstructor);
                        foreach (var next in targets.Where(item => !item.IsFactory).SelectMany(item => item.ImplementationDependencies).Distinct(StringComparer.Ordinal))
                        {
                            queue.Enqueue((next, path + " → " + next));
                        }
                    }
                }
            }
        }, cancellationToken);
}
