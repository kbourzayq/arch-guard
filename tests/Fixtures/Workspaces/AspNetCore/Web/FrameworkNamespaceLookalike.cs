using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Fakes;

namespace Microsoft.AspNetCore.Fakes
{
    /// <summary>Source code in a framework namespace imitates the convention without authorizing anything.</summary>
    public static class NamespaceLookalike
    {
        public static RouteHandlerBuilder RequireAuthorization(this RouteHandlerBuilder builder) => builder;
    }
}

namespace Web
{
    public static class NamespaceLookalikeEndpoints
    {
        // ASP002 proven: a framework namespace in source does not make the method the framework convention.
        public static void Map(WebApplication app) => app.MapGet("/fakens", () => "fakens").RequireAuthorization();
    }
}
