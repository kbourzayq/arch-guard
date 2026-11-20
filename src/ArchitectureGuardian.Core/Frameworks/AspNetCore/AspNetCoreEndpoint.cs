using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Frameworks.AspNetCore;

/// <summary>An HTTP endpoint found in source, with its declared authorization and semantic dependencies.</summary>
public sealed record AspNetCoreEndpoint
{
    /// <summary>Creates an immutable endpoint snapshot.</summary>
    /// <param name="project">The policy-relative project containing the endpoint.</param>
    /// <param name="kind">The programming model that declares the endpoint.</param>
    /// <param name="symbol">The action method or mapped route that identifies the endpoint.</param>
    /// <param name="authorization">The statically declared authorization.</param>
    /// <param name="dependencies">Types used by the endpoint, in any order.</param>
    /// <param name="location">The declaration or mapping site, when available.</param>
    /// <exception cref="ArgumentException">A required value is blank or the dependency list contains null.</exception>
    public AspNetCoreEndpoint(string project, AspNetCoreEndpointKind kind, string symbol, DeclaredAuthorization authorization,
        IEnumerable<AspNetCoreDependency> dependencies, SourceLocation? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(dependencies);
        Project = project;
        Kind = kind;
        Symbol = symbol;
        Authorization = authorization;
        Dependencies = Array.AsReadOnly(dependencies.ToArray());
        if (Dependencies.Any(item => item is null))
        {
            throw new ArgumentException("Dependencies cannot contain null.", nameof(dependencies));
        }

        Location = location;
    }

    /// <summary>Gets the policy-relative project containing the endpoint.</summary>
    public string Project { get; }
    /// <summary>Gets the programming model that declares the endpoint.</summary>
    public AspNetCoreEndpointKind Kind { get; }
    /// <summary>Gets the identifying action method or mapped route.</summary>
    public string Symbol { get; }
    /// <summary>Gets the statically declared authorization.</summary>
    public DeclaredAuthorization Authorization { get; }
    /// <summary>Gets the types used by the endpoint.</summary>
    public IReadOnlyList<AspNetCoreDependency> Dependencies { get; }
    /// <summary>Gets the declaration or mapping site, or null when unavailable.</summary>
    public SourceLocation? Location { get; }
}
