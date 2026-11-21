using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Acme.Catalog;

// Compliant: the class-level attribute authorizes every exposed method, and only DTOs cross the boundary.
[Authorize("Catalog.Books")]
public class BookAppService(IBookRepository books) : ApplicationService, IBookAppService
{
    public async Task<BookDto> GetAsync(Guid id) => Map(await books.GetAsync(id));

    public async Task<PagedResultDto<BookDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        var items = await books.GetPagedListAsync(input.SkipCount, input.MaxResultCount, input.Sorting ?? nameof(Book.Name));
        return new(await books.GetCountAsync(), items.Select(Map).ToList());
    }

    private static BookDto Map(Book book) => new() { Id = book.Id, Name = book.Name, Type = book.Type };
}
