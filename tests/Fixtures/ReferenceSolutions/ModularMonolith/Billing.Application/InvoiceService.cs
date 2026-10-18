namespace Billing.Application;

public sealed class InvoiceService : Billing.Contracts.IInvoice
{
    public void Pay() { }
#if AG003
    public void Notify(Orders.Contracts.IOrder order) => order.Submit();
#endif
}
