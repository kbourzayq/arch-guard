using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.Abp;

/// <summary>Checks ABP004: every remotely exposable application service method declares its authorization in source.</summary>
/// <remarks>
/// ABP's conventional controllers can publish application service methods as HTTP endpoints. A method without a declaration
/// is a potential security concern: global policies, middleware or route configuration may still protect it at runtime.
/// </remarks>
public sealed class ApplicationServiceAuthorizationRule : IArchitectureRule<DependencyBuildResult>
{
    /// <inheritdoc/>
    public RuleDescriptor Descriptor => AbpRuleCatalog.ApplicationServiceAuthorization;

    /// <summary>Reports each exposable method that has no authorization attribute, permission check or CRUD policy name.</summary>
    /// <param name="context">The repository, ABP facts and upstream coverage.</param>
    /// <param name="cancellationToken">Cooperative cancellation during evaluation.</param>
    /// <returns>Potential findings with source evidence, or an incomplete result when facts are unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupts evaluation.</exception>
    public Task<RuleEvaluationResult> EvaluateAsync(DependencyBuildResult context, CancellationToken cancellationToken = default)
        => AbpEvaluation.Run(context, Descriptor, (repository, facts, violations, issues) =>
        {
            foreach (var group in facts.ServiceMethods.Where(item => item.Authorization == AbpAuthorizationDeclaration.None)
                         .GroupBy(item => (item.Project, item.Service, item.Method)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (project, service, method) = group.Key;
                if (AbpEvaluation.Mapping(repository, project, Descriptor.Id, issues) is not { } source)
                {
                    continue;
                }

                var inherited = group.Any(item => item.IsInherited);
                var violation = $"Application service method '{service}.{method}' declares no authorization{(inherited ? " (inherited from an ABP base class)" : "")}.";
                violations.Add(AbpEvaluation.Violation(Descriptor, source.ModuleId, project, service,
                    $"{violation} ABP can expose it as an HTTP endpoint; this is a potential concern that static analysis cannot confirm, because global policies or route configuration may protect it.",
                    inherited
                        ? $"Assign the matching policy name, such as GetListPolicyName, in '{service}', add [Authorize] to the class, or mark intentionally public access with [AllowAnonymous]."
                        : "Add [Authorize] with a permission, call AuthorizationService.CheckAsync, or mark intentionally public access with [AllowAnonymous]. Confirm the effective authorization with integration tests.",
                    group.Select(item => new DiagnosticEvidence(violation, item.Location)), ViolationClassification.Potential,
                    "authorization", ["[Authorize]", "[AllowAnonymous]", "permission check"], service + "." + method));
            }
        }, cancellationToken);
}
