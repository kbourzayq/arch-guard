using Volo.Abp.Application;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Modularity;

namespace Acme.Ordering;

[DependsOn(typeof(AbpDddApplicationContractsModule))]
public class OrderingApplicationContractsModule : AbpModule;

public class OrderDto : EntityDto<Guid>
{
    public Guid BookId { get; set; }
}
