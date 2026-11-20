using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.AspNetCore;

/// <summary>Checks ASP001: controller actions must not use persistence technology directly.</summary>
/// <remarks>Findings are proven from bound symbols; persistence reached through other abstractions is out of scope.</remarks>
public sealed class ControllerPersistenceRule : IArchitectureRule<DependencyBuildResult>
{
    /// <inheritdoc/>
    public RuleDescriptor Descriptor => AspNetCoreRuleCatalog.ControllerPersistence;

    /// <summary>Reports each persistence type used by a controller action.</summary>
    /// <param name="context">The repository, ASP.NET Core facts and upstream coverage.</param>
    /// <param name="cancellationToken">Cooperative cancellation during evaluation.</param>
    /// <returns>Proven findings with source evidence, or an incomplete result when facts are unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupts evaluation.</exception>
    public Task<RuleEvaluationResult> EvaluateAsync(DependencyBuildResult context, CancellationToken cancellationToken = default)
        => AspNetCoreEvaluation.Run(context, Descriptor, (repository, facts, violations, issues) =>
            PersistenceFindings.Add(Descriptor, [AspNetCoreEndpointKind.Controller, AspNetCoreEndpointKind.ControllerAction], "controller", repository, facts, violations, issues, cancellationToken),
            cancellationToken);
}
