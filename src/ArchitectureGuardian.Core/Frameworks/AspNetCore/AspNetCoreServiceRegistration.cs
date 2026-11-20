using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Frameworks.AspNetCore;

/// <summary>A service registration found in source, retaining only what lifetime checks can prove.</summary>
public sealed record AspNetCoreServiceRegistration
{
    /// <summary>Creates an immutable registration snapshot.</summary>
    /// <param name="project">The policy-relative project containing the registration.</param>
    /// <param name="lifetime">The declared lifetime.</param>
    /// <param name="serviceType">The display name of the registered service type.</param>
    /// <param name="implementationType">The concrete type, or null when a factory or instance supplies it.</param>
    /// <param name="implementationDependencies">Constructor parameter types of the implementation; empty when unknown.</param>
    /// <param name="location">The registration call, when available.</param>
    /// <param name="hasAmbiguousConstructor">Whether several public constructors make the dependencies a best estimate.</param>
    /// <exception cref="ArgumentException">A required value is blank.</exception>
    public AspNetCoreServiceRegistration(string project, AspNetCoreServiceLifetime lifetime, string serviceType,
        string? implementationType, IEnumerable<string> implementationDependencies, SourceLocation? location = null,
        bool hasAmbiguousConstructor = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceType);
        ArgumentNullException.ThrowIfNull(implementationDependencies);
        Project = project;
        Lifetime = lifetime;
        ServiceType = serviceType;
        ImplementationType = implementationType;
        ImplementationDependencies = Array.AsReadOnly(implementationDependencies.ToArray());
        Location = location;
        HasAmbiguousConstructor = hasAmbiguousConstructor;
    }

    /// <summary>Gets the policy-relative project containing the registration.</summary>
    public string Project { get; }
    /// <summary>Gets the declared lifetime.</summary>
    public AspNetCoreServiceLifetime Lifetime { get; }
    /// <summary>Gets the display name of the registered service type.</summary>
    public string ServiceType { get; }
    /// <summary>Gets the concrete type, or null when a factory or instance supplies it.</summary>
    public string? ImplementationType { get; }
    /// <summary>Gets the constructor parameter types of the implementation, empty when unknown.</summary>
    public IReadOnlyList<string> ImplementationDependencies { get; }
    /// <summary>Gets whether a factory delegate hides what the implementation depends on.</summary>
    public bool IsFactory => ImplementationType is null;
    /// <summary>Gets whether several public constructors make the dependency list a best estimate.</summary>
    public bool HasAmbiguousConstructor { get; }
    /// <summary>Gets the registration call, or null when unavailable.</summary>
    public SourceLocation? Location { get; }
}
