namespace Slices.Host;

public static class Composition
{
    public static decimal Run(Ordering.Contracts.IOrdering ordering, Catalog.Contracts.ProductDto product) =>
        ordering.Quote(product.Id, product.Price, 1);
}
