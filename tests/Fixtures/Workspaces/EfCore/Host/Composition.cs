using Billing.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Orders.Infrastructure;

namespace Host;

// Allowed: registering each module's context, including option lambdas, is composition rather than data access.
public static class Composition
{
    public static IServiceCollection AddPersistence(this IServiceCollection services) => services
        .AddDbContext<OrdersDbContext>(options => options.EnableSensitiveDataLogging())
        .AddDbContextPool<BillingDbContext>(options => options.EnableDetailedErrors());

    // Allowed for EF001 and EF002: a conditional registration is still composition.
    public static IServiceCollection? AddOptionalPersistence(this IServiceCollection? services)
        => services?.AddDbContext<BillingDbContext>();

    // EF001 and EF002 (proven): a registration callback that uses another module's context is data access, not configuration.
    public static IServiceCollection AddSeededPersistence(this IServiceCollection services)
        => services.AddDbContext<OrdersDbContext>((provider, options) => options.UseSeeding((context, _) => BillingContexts.Open().SaveChanges()));
}
