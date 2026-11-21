using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace Acme.Catalog;

// Compliant: switching to a specific tenant and disabling soft-delete do not bypass tenant isolation.
public class CatalogDataSeeder(IBookRepository books, ICurrentTenant currentTenant, IDataFilter dataFilter) : ITransientDependency
{
    public async Task SeedAsync(Guid tenantId)
    {
        using (currentTenant.Change(name: null, id: tenantId))
        using (dataFilter.Disable<ISoftDelete>())
        {
            await books.InsertAsync(new Book { Name = "Seed" });
        }
    }
}
