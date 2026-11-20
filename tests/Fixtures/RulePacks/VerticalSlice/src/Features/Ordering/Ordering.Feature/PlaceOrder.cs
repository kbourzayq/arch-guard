namespace Ordering.Feature.PlaceOrder;

public sealed class OrderLine : Shop.SharedKernel.Entity
{
    public decimal Amount { get; init; }
}

// Approved slice dependency: Ordering uses Catalog's declared contract and the shared kernel.
public sealed class PlaceOrderHandler : Ordering.Contracts.IOrdering
{
    public decimal Quote(Guid productId, decimal unitPrice, int quantity) =>
        Line(new Catalog.Contracts.ProductDto(productId, "", unitPrice), quantity).Amount;

    private static OrderLine Line(Catalog.Contracts.ProductDto product, int quantity) => new() { Amount = product.Price * quantity };
#if AG004
    public string BypassContract(Guid id) => new Catalog.Feature.Products.ProductRepository().Find(id).Name;
#endif
}
