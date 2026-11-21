using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Services;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Acme.Ordering;

[Authorize("Ordering.Reports")]
public class OrderReportAppService(IRepository<Order, Guid> orders) : ApplicationService
{
    // ABP005: disabling the multi-tenant filter reads every tenant's orders.
    public async Task<long> CountAllTenantsAsync()
    {
        using (DataFilter.Disable<IMultiTenant>())
        {
            return await orders.GetCountAsync();
        }
    }

    // ABP005: a null tenant ID passed by name, after the name argument, is still a host switch.
    public async Task<long> CountHostByNameAsync()
    {
        using (CurrentTenant.Change(name: "host", id: null))
        {
            return await orders.GetCountAsync();
        }
    }

    // ABP005: switching to the host context leaves the current tenant's isolation.
    public async Task<long> CountHostAsync()
    {
        using (CurrentTenant.Change(null))
        {
            return await orders.GetCountAsync();
        }
    }
}
