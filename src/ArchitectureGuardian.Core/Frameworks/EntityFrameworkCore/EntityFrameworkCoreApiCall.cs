using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Frameworks.EntityFrameworkCore;

/// <summary>One source reference to an EF Core method, such as an IgnoreQueryFilters or FromSqlRaw call.</summary>
public sealed record EntityFrameworkCoreApiCall
{
    /// <summary>Creates an immutable method-reference snapshot.</summary>
    /// <param name="project">The policy-relative project containing the reference.</param>
    /// <param name="user">The type whose source contains the reference.</param>
    /// <param name="method">The simple method name that policies match.</param>
    /// <param name="symbol">The display name of the method's containing type and name, for explanations.</param>
    /// <param name="location">The reference site, when available.</param>
    /// <exception cref="ArgumentException">A required value is blank.</exception>
    public EntityFrameworkCoreApiCall(string project, string user, string method, string symbol, SourceLocation? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        Project = project;
        User = user;
        Method = method;
        Symbol = symbol;
        Location = location;
    }

    /// <summary>Gets the policy-relative project containing the reference.</summary>
    public string Project { get; }
    /// <summary>Gets the type whose source contains the reference.</summary>
    public string User { get; }
    /// <summary>Gets the simple method name.</summary>
    public string Method { get; }
    /// <summary>Gets the containing type and method name.</summary>
    public string Symbol { get; }
    /// <summary>Gets the reference site, or null when unavailable.</summary>
    public SourceLocation? Location { get; }
}
