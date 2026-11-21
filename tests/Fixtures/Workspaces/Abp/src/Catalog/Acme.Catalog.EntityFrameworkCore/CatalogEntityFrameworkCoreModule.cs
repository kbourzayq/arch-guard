using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace Acme.Catalog;

[DependsOn(typeof(AbpEntityFrameworkCoreModule), typeof(CatalogDomainModule))]
public class CatalogEntityFrameworkCoreModule : AbpModule;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : AbpDbContext<CatalogDbContext>(options)
{
    public DbSet<Book> Books => Set<Book>();
}

public class EfCoreBookRepository(IDbContextProvider<CatalogDbContext> provider)
    : EfCoreRepository<CatalogDbContext, Book, Guid>(provider), IBookRepository
{
    public async Task<Book?> FindByNameAsync(string name)
        => await (await GetDbSetAsync()).FirstOrDefaultAsync(book => book.Name == name);
}
