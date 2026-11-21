using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Acme.Catalog;

public class Book : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public string Name { get; set; } = "";
    public BookType Type { get; set; }
}

public interface IBookRepository : IRepository<Book, Guid>
{
    Task<Book?> FindByNameAsync(string name);
}
