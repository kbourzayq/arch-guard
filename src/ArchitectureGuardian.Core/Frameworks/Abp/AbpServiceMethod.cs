using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Frameworks.Abp;

/// <summary>One public application service method that ABP can expose remotely, with its declared authorization.</summary>
public sealed record AbpServiceMethod
{
    /// <summary>Creates an immutable method snapshot.</summary>
    /// <param name="project">The policy-relative project that declares the service.</param>
    /// <param name="service">The display name of the concrete application service class.</param>
    /// <param name="method">The method name and parameter types, such as GetAsync(System.Guid), which keep overloads apart.</param>
    /// <param name="authorization">How the method declares its authorization.</param>
    /// <param name="isInherited">Whether the method is inherited from an ABP base class, such as CrudAppService.</param>
    /// <param name="location">The method declaration, or the service declaration for inherited methods, when available.</param>
    /// <exception cref="ArgumentException">A required value is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="authorization"/> is undefined.</exception>
    public AbpServiceMethod(string project, string service, string method, AbpAuthorizationDeclaration authorization,
        bool isInherited = false, SourceLocation? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        if (!Enum.IsDefined(authorization))
        {
            throw new ArgumentOutOfRangeException(nameof(authorization));
        }

        Project = project;
        Service = service;
        Method = method;
        Authorization = authorization;
        IsInherited = isInherited;
        Location = location;
    }

    /// <summary>Gets the policy-relative project that declares the service.</summary>
    public string Project { get; }
    /// <summary>Gets the concrete application service display name.</summary>
    public string Service { get; }
    /// <summary>Gets the method name and parameter types.</summary>
    public string Method { get; }
    /// <summary>Gets how the method declares its authorization.</summary>
    public AbpAuthorizationDeclaration Authorization { get; }
    /// <summary>Gets whether the method is inherited from an ABP base class.</summary>
    public bool IsInherited { get; }
    /// <summary>Gets the declaration site, or null when unavailable.</summary>
    public SourceLocation? Location { get; }
}
