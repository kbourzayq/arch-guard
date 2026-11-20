namespace Orders.Application;

public sealed class Checkout : Orders.Contracts.IOrder
{
    private readonly Orders.Domain.Order _order = new(10m);

    public decimal Total => _order.Total;

    // Public contract use across modules is permitted by contract:Billing/api.
    public void Pay(Billing.Contracts.IInvoice invoice) => invoice.Pay(Total);
#if AG004
    public decimal BypassContract() => new Billing.Application.InvoiceCalculator().Calculate(Total);
#endif
}
