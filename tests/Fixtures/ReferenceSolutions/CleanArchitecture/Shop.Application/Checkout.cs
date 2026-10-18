namespace Shop.Application;

public sealed class Checkout
{
    public void Submit(Shop.Domain.IOrderStore store) => store.Save();
#if AG002
    public void BypassPort() => new Shop.Infrastructure.OrderStore().Save();
#endif
#if INCOMPLETE
    public MissingType Broken() => new();
#endif
}
