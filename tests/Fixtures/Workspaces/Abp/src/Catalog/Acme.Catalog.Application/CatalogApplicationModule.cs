using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace Acme.Catalog;

[DependsOn(typeof(AbpDddApplicationModule), typeof(CatalogDomainModule), typeof(CatalogApplicationContractsModule))]
public class CatalogApplicationModule : AbpModule;
