namespace Shop.Host;

public static class Composition
{
    public static void Run() => new Shop.Application.Checkout().Submit(new Shop.Infrastructure.OrderStore());
}
