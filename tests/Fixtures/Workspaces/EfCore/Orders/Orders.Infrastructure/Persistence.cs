using Billing.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Orders.Application;
using Orders.Domain;
using BillingFactory = Microsoft.EntityFrameworkCore.IDbContextFactory<Billing.Infrastructure.BillingDbContext>;

namespace Orders.Infrastructure;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Order>().HasQueryFilter(order => order.TenantId == "current");
}

// Allowed: the owning module's repository uses its own DbContext in the persistence layer.
public sealed class OrderRepository(OrdersDbContext context) : IOrderRepository
{
    public Task<Order?> FindAsync(int id, CancellationToken cancellationToken)
        => context.Orders.FirstOrDefaultAsync(order => order.Id == id, cancellationToken);
}

// EF001 (proven): Orders uses the DbContext that Billing owns.
public sealed class CrossModuleReport(BillingDbContext billing)
{
    public int Count() => billing.Invoices.Count();
}

// EF001 (proven): the bound return type shows that the accessor supplies Billing's context.
public sealed class AccessorReport
{
    public int Save() => BillingContexts.Open().SaveChanges();
}

// EF001 (proven): the alias hides the generic argument, but the bound type still names Billing's context.
public sealed class AliasReport(BillingFactory factory)
{
    public BillingFactory Factory => factory;
}

// EF001 (proven): an indexer supplies Billing's context, although no name in the expression is EF Core.
public sealed class IndexerReport(BillingContextStore store)
{
    public int Save() => store[0].SaveChanges();
}

// EF001 (runtime validation required): the base type hides which module's context is supplied.
public sealed class GenericRepository(DbContext context)
{
    public IQueryable<T> Query<T>() where T : class => context.Set<T>();
}

public sealed class AdminOrderQueries(OrdersDbContext context)
{
    // EF003 (potential): bypasses query filters; this does not prove tenant-data exposure.
    public IQueryable<Order> AllTenants() => context.Orders.IgnoreQueryFilters();

    // EF003 (proven): raw SQL is forbidden by the policy.
    public IQueryable<Order> Raw() => context.Orders.FromSqlRaw("SELECT * FROM Orders");
}
