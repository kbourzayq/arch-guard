using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.Abp;

/// <summary>Checks ABP005: code outside the declared bypass layers does not leave the current tenant's data isolation.</summary>
/// <remarks>
/// Disabling the IMultiTenant filter or switching to the host is policy evidence for review. Whether another tenant's data
/// is read or returned depends on runtime data, so findings are potential.
/// </remarks>
public sealed class TenantIsolationRule : IArchitectureRule<DependencyBuildResult>
{
    /// <inheritdoc/>
    public RuleDescriptor Descriptor => AbpRuleCatalog.TenantIsolation;

    /// <summary>Reports each type outside abp.tenantFilterBypassLayers that bypasses tenant isolation.</summary>
    /// <param name="context">The repository, ABP facts and upstream coverage.</param>
    /// <param name="cancellationToken">Cooperative cancellation during evaluation.</param>
    /// <returns>Potential findings with call-site evidence, or an incomplete result when facts are unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupts evaluation.</exception>
    public Task<RuleEvaluationResult> EvaluateAsync(DependencyBuildResult context, CancellationToken cancellationToken = default)
        => AbpEvaluation.Run(context, Descriptor, (repository, facts, violations, issues) =>
        {
            var layers = repository.ResolvedPolicy.Policy.Abp?.TenantFilterBypassLayers ?? [];
            foreach (var group in facts.TenantBypasses.GroupBy(item => (item.Project, item.User, item.Kind)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (project, user, kind) = group.Key;
                if (AbpEvaluation.Mapping(repository, project, Descriptor.Id, issues) is not { } source
                    || layers.Contains(source.LayerId, StringComparer.Ordinal))
                {
                    continue;
                }

                var api = string.Join("', '", group.Select(item => item.Api).Distinct(StringComparer.Ordinal));
                var effect = kind == AbpTenantBypassKind.DisableMultiTenantFilter
                    ? "disables the multi-tenant data filter, so queries can return every tenant's rows"
                    : "switches to the host context, leaving the current tenant's isolation";
                var violation = $"Type '{user}' in layer '{source.LayerId}' calls '{api}', which {effect}.";
                violations.Add(AbpEvaluation.Violation(Descriptor, source.ModuleId, project, user,
                    $"{violation} This is policy evidence for review and does not prove that another tenant's data is exposed.",
                    "Keep tenant isolation in effect, move the cross-tenant work into a layer listed in abp.tenantFilterBypassLayers, or confirm with tests that no other tenant's data reaches the caller.",
                    group.Select(item => new DiagnosticEvidence($"Type '{user}' calls '{item.Api}'.", item.Location)), ViolationClassification.Potential,
                    "tenantFilterBypassLayers", layers, kind == AbpTenantBypassKind.DisableMultiTenantFilter ? "IDataFilter.Disable<IMultiTenant>" : "ICurrentTenant.Change(null)"));
            }
        }, cancellationToken);
}
