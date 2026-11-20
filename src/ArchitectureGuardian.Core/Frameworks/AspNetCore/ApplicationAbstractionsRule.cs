using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.AspNetCore;

/// <summary>Checks ASP005: endpoints must receive application abstractions, not concrete services from other projects.</summary>
/// <remarks>Only injected concrete classes declared in another source project are reported; persistence belongs to ASP001 and ASP004.</remarks>
public sealed class ApplicationAbstractionsRule : IArchitectureRule<DependencyBuildResult>
{
    /// <inheritdoc/>
    public RuleDescriptor Descriptor => AspNetCoreRuleCatalog.ApplicationAbstractions;

    /// <summary>Reports injected concrete types that cross a project boundary.</summary>
    /// <param name="context">The repository, ASP.NET Core facts and upstream coverage.</param>
    /// <param name="cancellationToken">Cooperative cancellation during evaluation.</param>
    /// <returns>Proven findings with source evidence, or an incomplete result when facts are unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupts evaluation.</exception>
    public Task<RuleEvaluationResult> EvaluateAsync(DependencyBuildResult context, CancellationToken cancellationToken = default)
        => AspNetCoreEvaluation.Run(context, Descriptor, (repository, facts, violations, issues) =>
        {
            foreach (var endpoint in facts.Endpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var concrete = endpoint.Dependencies.Where(item => item.IsInjected && !item.IsAbstraction
                    && item.DeclaringProject is not null && item.DeclaringProject != endpoint.Project && !AspNetCoreSemantics.IsPersistence(item)).ToArray();
                if (concrete.Length == 0 || AspNetCoreEvaluation.Module(repository, endpoint.Project, Descriptor.Id, issues) is not { } module)
                {
                    continue;
                }

                foreach (var group in concrete.GroupBy(item => item.Symbol, StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    var message = $"Endpoint '{endpoint.Symbol}' in project '{endpoint.Project}' receives concrete type '{group.Key}' from project '{group.First().DeclaringProject}'.";
                    violations.Add(AspNetCoreEvaluation.Violation(Descriptor, module, endpoint.Project, endpoint.Symbol, message,
                        $"Depend on an interface or abstract class for '{group.Key}' and register the implementation in the composition root.",
                        group.Select(item => new DiagnosticEvidence(message, item.Location ?? endpoint.Location)),
                        ViolationClassification.Proven, "injectedDependencies", ["abstraction"], group.Key));
                }
            }
        }, cancellationToken);
}
