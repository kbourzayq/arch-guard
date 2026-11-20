namespace Billing.Contracts;

public interface IInvoice
{
    void Pay(decimal amount);
}
