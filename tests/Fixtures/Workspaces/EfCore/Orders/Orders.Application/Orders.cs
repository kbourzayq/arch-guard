using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Lookalikes;
using Orders.Domain;

namespace Orders.Application;

// Allowed: application code depends on a repository contract, not on EF Core.
public interface IOrderRepository
{
    Task<Order?> FindAsync(int id, CancellationToken cancellationToken);
}

public sealed class OrderService(IOrderRepository orders)
{
    public Task<Order?> GetAsync(int id, CancellationToken cancellationToken) => orders.FindAsync(id, cancellationToken);
}

// EF002 (proven): an EF Core extension method is used outside the persistence layer.
public sealed class LeakyOrderQuery
{
    public Task<List<Order>> RunAsync(IQueryable<Order> orders) => orders.ToListAsync();
}

// Not EF Core: a source method in an EF Core namespace is a lookalike, so EF002 and EF003 ignore it.
public sealed class LookalikeUser
{
    public void Run() => "orders".IgnoreQueryFilters();
}
