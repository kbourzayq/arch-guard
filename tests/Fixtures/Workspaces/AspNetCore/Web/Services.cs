using Application;
using Microsoft.Extensions.DependencyInjection;

namespace Web;

public interface IScopedThing { }
public class ScopedThing : IScopedThing { }
public interface ISingletonThing { }
public class SingletonThing(IScopedThing scoped) : ISingletonThing { }
public interface IMiddle { }
public class Middle(IScopedThing scoped) : IMiddle { }
public interface IViaTransient { }
public class ViaTransient(IMiddle middle) : IViaTransient { }
public interface IMixed { }
public class Mixed : IMixed { }
public interface IHolder { }
public class Holder(IMixed mixed) : IHolder { }
public interface IFactoryThing { }
public class FactoryThing : IFactoryThing { }
public interface IFine { }
public class Fine(IFineDependency dependency) : IFine { }
public interface IFineDependency { }
public class FineDependency : IFineDependency { }
public interface IHandler { }
public class Handler : IHandler { }
public interface IHandlerHost { }
public class HandlerHost(IEnumerable<IHandler> handlers) : IHandlerHost { }
public interface ITwoCtor { }
public class TwoCtor : ITwoCtor { public TwoCtor() { } public TwoCtor(IScopedThing scoped) { } }
public interface IRemoteHolder { }
public class RemoteHolder(IRemoteScoped remote) : IRemoteHolder { }
public interface IInstance { }
public class Instance : IInstance { }

public static class Registration
{
    public static void Add(IServiceCollection services)
    {
        services.AddScoped<IScopedThing, ScopedThing>();
        services.AddSingleton<ISingletonThing, SingletonThing>(); // ASP003 proven
        services.AddTransient<IMiddle, Middle>();
        services.AddSingleton<IViaTransient, ViaTransient>(); // ASP003 proven through a transient
        services.AddScoped<IMixed, Mixed>();
        services.AddTransient<IMixed, Mixed>();
        services.AddSingleton<IHolder, Holder>(); // ASP003 potential
        services.AddSingleton<IFactoryThing>(provider => new FactoryThing()); // ASP003 runtime validation
        services.AddSingleton<IFineDependency, FineDependency>();
        services.AddSingleton<IFine, Fine>(); // allowed
        services.AddScoped<IHandler, Handler>();
        services.AddSingleton<IHandlerHost, HandlerHost>(); // ASP003 proven through collection injection
        services.AddSingleton<ITwoCtor, TwoCtor>(); // ASP003 potential: constructor choice is ambiguous
        services.AddSingleton<IRemoteHolder, RemoteHolder>(); // ASP003 potential: the scoped registration is in another project
        services.AddSingleton<IInstance>(new Instance()); // allowed: an instance cannot capture scoped services
    }
}
