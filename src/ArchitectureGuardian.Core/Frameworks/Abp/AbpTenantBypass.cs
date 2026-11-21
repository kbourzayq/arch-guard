using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Frameworks.Abp;

/// <summary>One source call that leaves the current tenant's data isolation.</summary>
public sealed record AbpTenantBypass
{
    /// <summary>Creates an immutable bypass snapshot.</summary>
    /// <param name="project">The policy-relative project containing the call.</param>
    /// <param name="user">The type whose source contains the call.</param>
    /// <param name="kind">How the call leaves tenant isolation.</param>
    /// <param name="api">The display name of the called ABP API.</param>
    /// <param name="location">The call site, when available.</param>
    /// <exception cref="ArgumentException">A required value is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is undefined.</exception>
    public AbpTenantBypass(string project, string user, AbpTenantBypassKind kind, string api, SourceLocation? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(api);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Project = project;
        User = user;
        Kind = kind;
        Api = api;
        Location = location;
    }

    /// <summary>Gets the policy-relative project containing the call.</summary>
    public string Project { get; }
    /// <summary>Gets the type whose source contains the call.</summary>
    public string User { get; }
    /// <summary>Gets how the call leaves tenant isolation.</summary>
    public AbpTenantBypassKind Kind { get; }
    /// <summary>Gets the called ABP API.</summary>
    public string Api { get; }
    /// <summary>Gets the call site, or null when unavailable.</summary>
    public SourceLocation? Location { get; }
}
