namespace Application;

/// <summary>A service that another project registers as scoped.</summary>
public interface IRemoteScoped { }

/// <summary>The implementation of <see cref="IRemoteScoped"/>.</summary>
public class RemoteScoped : IRemoteScoped { }
