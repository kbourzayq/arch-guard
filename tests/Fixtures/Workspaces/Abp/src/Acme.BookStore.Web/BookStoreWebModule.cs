using Acme.Catalog;
using Acme.Ordering;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Modularity;

namespace Acme.BookStore.Web;

// Compliant: the host declares every referenced module, and OrderingApplicationModule's own gap is reported once.
[DependsOn(typeof(AbpAspNetCoreMvcModule), typeof(CatalogApplicationModule), typeof(CatalogEntityFrameworkCoreModule),
    typeof(CatalogHttpApiModule), typeof(OrderingApplicationModule))]
public class BookStoreWebModule : AbpModule;
