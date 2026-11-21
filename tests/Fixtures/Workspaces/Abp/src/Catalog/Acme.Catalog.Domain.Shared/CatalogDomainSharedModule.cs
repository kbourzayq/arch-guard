using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Acme.Catalog;

[DependsOn(typeof(AbpDddDomainSharedModule))]
public class CatalogDomainSharedModule : AbpModule;

public enum BookType
{
    Novel,
    Science,
}
