namespace Orders.Application;

public class SubmitOrder
{
    public decimal Total(Billing.Contracts.IInvoice invoice) => invoice.Total;
}
