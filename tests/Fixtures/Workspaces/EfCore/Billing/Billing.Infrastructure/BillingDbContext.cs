using Microsoft.EntityFrameworkCore;

namespace Billing.Infrastructure;

public sealed class Invoice
{
    public int Id { get; set; }
}

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
}

public static class BillingContexts
{
    public static BillingDbContext Open() => new(new DbContextOptions<BillingDbContext>());
}

public sealed class BillingContextStore
{
    public BillingDbContext this[int index] => BillingContexts.Open();
}
