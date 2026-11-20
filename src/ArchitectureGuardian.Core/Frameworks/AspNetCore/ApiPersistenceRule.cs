using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.AspNetCore;

/// <summary>Checks ASP004: minimal API handlers must not use persistence technology directly.</summary>
/// <remarks>Findings are proven from bound symbols in handler parameters and bodies.</remarks>
public sealed class ApiPersistenceRule : IArchitectureRule<DependencyBuildResult>
{
    /// <inheritdoc/>
    public RuleDescriptor Descriptor => AspNetCoreRuleCatalog.ApiPersistence;

    /// <summary>Reports each persistence type used by a minimal API handler.</summary>
    /// <param name="context">The repository, ASP.NET Core facts and upstream coverage.</param>
    /// <param name="cancellationToken">Cooperative cancellation during evaluation.</param>
    /// <returns>Proven findings with source evidence, or an incomplete result when facts are unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupts evaluation.</exception>
    public Task<RuleEvaluationResult> EvaluateAsync(DependencyBuildResult context, CancellationToken cancellationToken = default)
        => AspNetCoreEvaluation.Run(context, Descriptor, (repository, facts, violations, issues) =>
            PersistenceFindings.Add(Descriptor, [AspNetCoreEndpointKind.MinimalApi], "minimal API handler", repository, facts, violations, issues, cancellationToken),
            cancellationToken);
}
