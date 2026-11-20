namespace ArchitectureGuardian.Core.Frameworks.AspNetCore;

/// <summary>Neutral ASP.NET Core facts extracted from compilations, independent of Roslyn and any host.</summary>
/// <remarks>Only statically visible source is represented; runtime composition is explicitly out of scope.</remarks>
public sealed class AspNetCoreFacts
{
    /// <summary>Snapshots the endpoints and registrations in deterministic order.</summary>
    /// <param name="endpoints">Endpoints found in solution source.</param>
    /// <param name="registrations">Service registrations found in solution source.</param>
    /// <exception cref="ArgumentNullException">A collection is null.</exception>
    public AspNetCoreFacts(IEnumerable<AspNetCoreEndpoint> endpoints, IEnumerable<AspNetCoreServiceRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(registrations);
        Endpoints = Array.AsReadOnly(endpoints
            .OrderBy(item => item.Project, StringComparer.Ordinal).ThenBy(item => item.Symbol, StringComparer.Ordinal)
            .ThenBy(item => item.Location?.CanonicalKey, StringComparer.Ordinal).ToArray());
        Registrations = Array.AsReadOnly(registrations
            .OrderBy(item => item.Project, StringComparer.Ordinal).ThenBy(item => item.ServiceType, StringComparer.Ordinal)
            .ThenBy(item => item.Location?.CanonicalKey, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Gets the endpoints in project and symbol order.</summary>
    public IReadOnlyList<AspNetCoreEndpoint> Endpoints { get; }
    /// <summary>Gets the service registrations in project and service order.</summary>
    public IReadOnlyList<AspNetCoreServiceRegistration> Registrations { get; }
}
