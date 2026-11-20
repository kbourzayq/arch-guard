namespace Shop.Web;

public static class Composition
{
    public static void Run() => new Shop.Application.Checkout(new Shop.Infrastructure.OrderStore()).Submit();
}
