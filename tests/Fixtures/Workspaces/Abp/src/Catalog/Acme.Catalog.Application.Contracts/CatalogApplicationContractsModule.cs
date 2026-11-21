using Volo.Abp.Application;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Modularity;

namespace Acme.Catalog;

[DependsOn(typeof(AbpDddApplicationContractsModule), typeof(CatalogDomainSharedModule))]
public class CatalogApplicationContractsModule : AbpModule;

public class BookDto : EntityDto<Guid>
{
    public string Name { get; set; } = "";
    public BookType Type { get; set; }
}

public interface IBookAppService : IApplicationService
{
    Task<BookDto> GetAsync(Guid id);
    Task<PagedResultDto<BookDto>> GetListAsync(PagedAndSortedResultRequestDto input);
}
