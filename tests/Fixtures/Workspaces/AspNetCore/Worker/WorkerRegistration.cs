using Application;
using Microsoft.Extensions.DependencyInjection;

namespace Worker;

/// <summary>Registers a scoped service in a different project, which may build a separate container.</summary>
public static class WorkerRegistration
{
    public static void Add(IServiceCollection services) => services.AddScoped<IRemoteScoped, RemoteScoped>();
}
