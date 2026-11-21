using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Acme.Catalog;

[DependsOn(typeof(AbpDddDomainModule), typeof(CatalogDomainSharedModule))]
public class CatalogDomainModule : AbpModule;
