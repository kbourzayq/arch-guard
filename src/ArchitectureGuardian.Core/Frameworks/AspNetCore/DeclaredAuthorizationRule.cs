using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.AspNetCore;

/// <summary>Checks ASP002: every endpoint must declare authorization or anonymous access in source.</summary>
/// <remarks>
/// A missing declaration is proven from source. Authorization that depends on a runtime fallback policy or on a
/// route group the analysis cannot resolve is reported separately as requiring runtime validation.
/// </remarks>
public sealed class DeclaredAuthorizationRule : IArchitectureRule<DependencyBuildResult>
{
    /// <inheritdoc/>
    public RuleDescriptor Descriptor => AspNetCoreRuleCatalog.DeclaredAuthorization;

    /// <summary>Reports endpoints without a declaration, and endpoints whose declaration cannot be resolved statically.</summary>
    /// <param name="context">The repository, ASP.NET Core facts and upstream coverage.</param>
    /// <param name="cancellationToken">Cooperative cancellation during evaluation.</param>
    /// <returns>Classified findings with source evidence, or an incomplete result when facts are unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupts evaluation.</exception>
    public Task<RuleEvaluationResult> EvaluateAsync(DependencyBuildResult context, CancellationToken cancellationToken = default)
        => AspNetCoreEvaluation.Run(context, Descriptor, (repository, facts, violations, issues) =>
        {
            foreach (var endpoint in facts.Endpoints.Where(item => item.Kind != AspNetCoreEndpointKind.Controller && item.Authorization is DeclaredAuthorization.Undeclared or DeclaredAuthorization.Indeterminate))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (AspNetCoreEvaluation.Module(repository, endpoint.Project, Descriptor.Id, issues) is not { } module)
                {
                    continue;
                }

                var proven = endpoint.Authorization == DeclaredAuthorization.Undeclared;
                var message = proven
                    ? $"Endpoint '{endpoint.Symbol}' in project '{endpoint.Project}' declares neither authorization nor anonymous access."
                    : $"Endpoint '{endpoint.Symbol}' in project '{endpoint.Project}' depends on a route group whose authorization cannot be resolved statically.";
                var remediation = proven
                    ? "Add [Authorize], RequireAuthorization() or an explicit [AllowAnonymous]/AllowAnonymous() declaration. A runtime fallback policy is not inferred, so declare the intent in source."
                    : "Declare authorization on the endpoint itself, create the group in the same method as its endpoints, or confirm the effective authorization with an integration test.";
                violations.Add(AspNetCoreEvaluation.Violation(Descriptor, module, endpoint.Project, endpoint.Symbol, message, remediation,
                    [new DiagnosticEvidence(message, endpoint.Location)],
                    proven ? ViolationClassification.Proven : ViolationClassification.RuntimeValidationRequired,
                    "declaredAuthorization", [endpoint.Authorization.ToString()], endpoint.Symbol));
            }
        }, cancellationToken);
}
