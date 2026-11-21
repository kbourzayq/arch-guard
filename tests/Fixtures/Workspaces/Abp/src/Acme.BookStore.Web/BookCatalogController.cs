using Acme.Catalog;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Domain.Repositories;

namespace Acme.BookStore.Web;

// ABP003: the host reads the repository directly, bypassing the application layer's authorization and DTOs.
public class BookCatalogController(IRepository<Book, Guid> books) : AbpController
{
    public async Task<long> CountAsync() => await books.GetCountAsync();
}
