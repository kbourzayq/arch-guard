namespace Billing.Application;

public sealed class InvoiceCalculator : Billing.Contracts.IInvoice
{
    public decimal Calculate(decimal amount) => amount * 1.2m;

    public void Pay(decimal amount) => Calculate(amount);
#if AG003
    public void Notify(Orders.Contracts.IOrder order) => Pay(order.Total);
#endif
}
