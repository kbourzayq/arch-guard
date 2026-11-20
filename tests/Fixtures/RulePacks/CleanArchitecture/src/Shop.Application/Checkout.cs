namespace Shop.Application;

public sealed class Checkout(Shop.Domain.IOrderStore store)
{
    public void Submit() => store.Save(new Shop.Domain.Order { Total = 10m });
#if AG002
    public void BypassPort() => new Shop.Infrastructure.OrderStore().Save(new Shop.Domain.Order());
#endif
}
