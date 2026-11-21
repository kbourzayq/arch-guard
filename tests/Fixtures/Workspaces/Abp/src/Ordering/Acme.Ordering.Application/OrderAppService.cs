using Acme.Catalog;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Acme.Ordering;

// ABP002: an application service DTO that carries an entity still exposes the entity.
public class OrderSummaryDto
{
    public Order Source { get; set; } = null!;
}

public class OrderReportDto
{
    public OrderReportLevel1 Deep { get; set; } = new();
    public OrderSourceDto Direct { get; set; } = new();
}

public class OrderReportLevel1
{
    public OrderReportLevel2 Next { get; set; } = new();
}

public class OrderReportLevel2
{
    public OrderReportLevel3 Next { get; set; } = new();
}

public class OrderReportLevel3
{
    public OrderSourceDto Source { get; set; } = new();
}

public class OrderSourceDto
{
    public Order? Source { get; set; }
}

public class OrderAppService(IRepository<Order, Guid> orders, IBookAppService books) : ApplicationService
{
    // ABP002: returns the Order entity instead of a DTO. Authorized by attribute.
    [Authorize("Ordering.Orders")]
    public Task<Order> GetAsync(Guid id) => orders.GetAsync(id);

    // ABP002 through the DTO property. Authorized by attribute.
    [Authorize("Ordering.Orders")]
    public async Task<OrderSummaryDto> GetSummaryAsync(Guid id) => new() { Source = await orders.GetAsync(id) };

    // Compliant: an imperative ABP permission check authorizes the method.
    public async Task<OrderDto> PlaceAsync(Guid bookId)
    {
        await AuthorizationService.CheckAsync("Ordering.Orders.Create");
        var book = await books.GetAsync(bookId);
        var order = await orders.InsertAsync(new Order { BookId = book.Id });
        return new() { Id = order.Id, BookId = order.BookId };
    }

    // ABP004: exposed by auto API controllers without any authorization declaration.
    public async Task CancelAsync(Guid id)
    {
        var order = await orders.GetAsync(id);
        order.IsCancelled = true;
    }

    // ABP004 again: an overload is a separate endpoint and a separate finding.
    public Task CancelAsync(string orderNumber) => Task.CompletedTask;

    // ABP002 through a DTO that is first reached at the depth limit and then through a shorter path.
    [Authorize("Ordering.Orders")]
    public Task<OrderReportDto> GetReportAsync() => Task.FromResult(new OrderReportDto());

    // Compliant: anonymous access is an explicit declaration.
    [AllowAnonymous]
    public Task<bool> IsOpenAsync() => Task.FromResult(true);

    // Not exposed: non-public members are not remote endpoints.
    protected Task RecalculateAsync() => Task.CompletedTask;
}

// Not exposed remotely, so ABP004 does not apply.
[RemoteService(false)]
public class OrderNumberAppService : ApplicationService
{
    public Task<string> NextAsync() => Task.FromResult("A-1");
}

// ABP004 for inherited CRUD methods: read and update policies are supplied; a null delete policy and a create
// override returning null supply none, so CreateAsync and DeleteAsync are exposed without a declaration.
public class OrderCrudAppService : CrudAppService<Order, OrderDto, Guid>
{
    public OrderCrudAppService(IRepository<Order, Guid> repository) : base(repository)
    {
        GetPolicyName = "Ordering.Orders";
        GetListPolicyName = "Ordering.Orders";
        DeletePolicyName = null;
    }

    protected override string? CreatePolicyName => null;

    protected override string? UpdatePolicyName => "Ordering.Orders.Update";
}

// Not exposed: integration services are internal by default, including services derived from one.
[IntegrationService]
public class OrderSyncAppService : ApplicationService
{
    public Task SyncAsync() => Task.CompletedTask;
}

public class OrderBackfillAppService : OrderSyncAppService
{
    public Task BackfillAsync() => Task.CompletedTask;
}

// Not exposed: ABP publishes only public, non-generic service types as conventional controllers.
internal class InternalOrderAppService : ApplicationService
{
    public Task PingAsync() => Task.CompletedTask;
}

public class GenericOrderAppService<T> : ApplicationService
{
    public Task PingAsync() => Task.CompletedTask;
}
