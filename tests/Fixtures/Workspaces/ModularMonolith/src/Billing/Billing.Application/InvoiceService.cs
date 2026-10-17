namespace Billing.Application;

public class InvoiceService
{
    public Billing.Domain.Invoice<int> Create() => new();
}
