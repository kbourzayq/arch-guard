using System.Data;
using Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;

namespace Web;

public static class Endpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/health", () => "ok").AllowAnonymous();
        app.MapGet("/secure", () => "secure").RequireAuthorization();
        app.MapGet("/open", () => "open"); // ASP002 proven
        app.MapGet("/data", (IDbConnection connection) => connection.State.ToString()).RequireAuthorization(); // ASP004
        app.MapGet("/svc", ([FromServices] ConcreteOrderService service) => "svc").RequireAuthorization(); // ASP005
        app.MapGet("/abstract", ([FromServices] IOrderService service) => "abstract").RequireAuthorization();
        var secured = app.MapGroup("/g").RequireAuthorization();
        secured.MapGet("/a", () => "a");
        var anonymous = app.MapGroup("/anon").AllowAnonymous();
        anonymous.MapGet("/b", () => "b");
        MapExternal(app.MapGroup("/ext"));
        var admin = app.MapGroup("/admin");
        admin.RequireAuthorization(); // a separate statement still configures the group
        admin.MapGet("/x", () => "x");
        app.MapGet(handler: ReadData, pattern: "/rd").RequireAuthorization(); // ASP004 through a reordered, named method group
    }

    private static string ReadData(IDbConnection connection) => connection.State.ToString();

    // ASP002 runtime validation: the group is configured by an unknown caller.
    public static void MapExternal(RouteGroupBuilder group) => group.MapGet("/e", () => "e");
}
