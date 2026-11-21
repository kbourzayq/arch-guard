using Acme.Catalog;

namespace Acme.BookStore.Web;

// Compliant: CountAsync is checked in the base class from another project, and the override keeps the base [Authorize].
public class StorefrontAppService : CatalogAppServiceBase
{
    public override Task<string> DescribeAsync() => Task.FromResult("storefront");
}
