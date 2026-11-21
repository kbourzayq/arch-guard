using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Modularity;

namespace Acme.Catalog;

[DependsOn(typeof(AbpAspNetCoreMvcModule), typeof(CatalogApplicationContractsModule))]
public class CatalogHttpApiModule : AbpModule;

// Compliant: the controller depends on the application service contract, not on repositories.
[Route("api/catalog/books")]
public class BookController(IBookAppService books) : AbpControllerBase
{
    [HttpGet("{id}")]
    public Task<BookDto> GetAsync(Guid id) => books.GetAsync(id);
}
