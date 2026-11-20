namespace Orders.Domain;

public sealed class Order(decimal total)
{
    public decimal Total { get; } = total;
}
