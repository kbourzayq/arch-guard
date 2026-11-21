using Volo.Abp.Http.Client;
using Volo.Abp.Modularity;

namespace Acme.Catalog;

[DependsOn(typeof(AbpHttpClientModule), typeof(CatalogApplicationContractsModule))]
public class CatalogHttpApiClientModule : AbpModule;
