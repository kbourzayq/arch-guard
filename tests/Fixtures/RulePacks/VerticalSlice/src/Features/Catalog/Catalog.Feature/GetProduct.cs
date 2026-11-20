namespace Catalog.Feature.GetProduct;

// Namespace dependencies inside one slice are ordinary implementation details, not violations.
public sealed class GetProductHandler(Catalog.Feature.Products.ProductRepository repository)
{
    public Catalog.Contracts.ProductDto Handle(Guid id)
    {
        var product = repository.Find(id);
        return new(product.Id, product.Name, product.Price);
    }
}
