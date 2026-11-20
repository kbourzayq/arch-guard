namespace Billing.Infrastructure;

public sealed class InvoiceStore
{
    public void Save(Billing.Application.InvoiceCalculator calculator) => calculator.Pay(0m);
}
