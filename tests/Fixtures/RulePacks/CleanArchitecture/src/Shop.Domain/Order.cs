namespace Shop.Domain;

public interface IOrderStore
{
    void Save(Order order);
}

public sealed class Order
{
    public decimal Total { get; init; }
#if AG006
    public System.Data.DataTable ToTable() => new();
#endif
}
