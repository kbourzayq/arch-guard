namespace Orders.Application;

public sealed class Checkout : Orders.Contracts.IOrder
{
    public void Submit() { }
    public void Pay(Billing.Contracts.IInvoice invoice) => invoice.Pay();
#if AG001
    public void BypassContract() => new Billing.Application.InvoiceService().Pay();
#endif
#if INCOMPLETE
    public Unavailable.Contract Broken() => new();
#endif
}
