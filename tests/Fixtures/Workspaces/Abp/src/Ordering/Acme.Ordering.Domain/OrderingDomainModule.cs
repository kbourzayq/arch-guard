using Volo.Abp.Domain;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;

namespace Acme.Ordering;

[DependsOn(typeof(AbpDddDomainModule))]
public class OrderingDomainModule : AbpModule;

public class Order : AggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid BookId { get; set; }
    public bool IsCancelled { get; set; }
}
