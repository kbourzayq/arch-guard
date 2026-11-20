namespace Catalog.Feature.Products;

public sealed class Product : Shop.SharedKernel.Entity
{
    public string Name { get; init; } = "";
    public decimal Price { get; init; }
}

public sealed class ProductRepository
{
    public Product Find(Guid id) => new() { Name = id.ToString(), Price = 1m };
}
