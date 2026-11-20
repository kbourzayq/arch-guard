namespace Shop.SharedKernel;

public abstract class Entity
{
    public Guid Id { get; } = Guid.NewGuid();
}
