namespace Monolith.Host;

public static class Composition
{
    public static void Run(Orders.Contracts.IOrder order, Billing.Contracts.IInvoice invoice) => invoice.Pay(order.Total);
}
