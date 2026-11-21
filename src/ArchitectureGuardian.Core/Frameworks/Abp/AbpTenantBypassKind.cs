namespace ArchitectureGuardian.Core.Frameworks.Abp;

/// <summary>Classifies a source construct that leaves the current tenant's data isolation.</summary>
public enum AbpTenantBypassKind
{
    /// <summary>Disabling the IMultiTenant data filter, which returns every tenant's rows.</summary>
    DisableMultiTenantFilter,
    /// <summary>Switching the current tenant to the host with ICurrentTenant.Change(null).</summary>
    ChangeToHost,
}
