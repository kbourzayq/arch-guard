namespace Orders.Domain;

public sealed class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "";
}
