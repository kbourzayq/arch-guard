using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;

namespace Acme.Catalog;

// A source base class reused by services in other projects, such as the host's storefront service.
public abstract class CatalogAppServiceBase : ApplicationService
{
    // Authorized imperatively; derived services in other projects inherit the check.
    public async Task<int> CountAsync()
    {
        await AuthorizationService.CheckAsync("Catalog.Books");
        return 0;
    }

    // Authorized by attribute; overrides inherit it, as ABP's interceptor reads inherited attributes.
    [Authorize("Catalog.Books")]
    public virtual Task<string> DescribeAsync() => Task.FromResult("catalog");
}
