using Microsoft.AspNetCore.Builder;
using Web.Fakes;

namespace Web.Fakes
{
    /// <summary>Imitates the framework convention without authorizing anything.</summary>
    public static class Lookalike
    {
        public static RouteHandlerBuilder RequireAuthorization(this RouteHandlerBuilder builder) => builder;
    }
}

namespace Web
{
    public static class LookalikeEndpoints
    {
        // ASP002 proven: the project-defined method is not the framework convention.
        public static void Map(WebApplication app) => app.MapGet("/fake", () => "fake").RequireAuthorization();
    }
}
