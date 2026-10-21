namespace Consumer;

public sealed class Checkout
{
    public void Pay() => new Library.Implementation.InvoiceService().Pay();
}
