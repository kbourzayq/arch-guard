namespace Ordering.Contracts;

public interface IOrdering
{
    decimal Quote(Guid productId, decimal unitPrice, int quantity);
}
