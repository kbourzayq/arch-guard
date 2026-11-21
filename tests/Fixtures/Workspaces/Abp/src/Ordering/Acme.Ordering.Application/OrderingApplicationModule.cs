using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace Acme.Ordering;

// ABP001: the project references Acme.Catalog.Application.Contracts, but no declared dependency reaches
// CatalogApplicationContractsModule, so ABP never initializes it for this module.
[DependsOn(typeof(AbpDddApplicationModule), typeof(OrderingDomainModule), typeof(OrderingApplicationContractsModule))]
public class OrderingApplicationModule : AbpModule;
