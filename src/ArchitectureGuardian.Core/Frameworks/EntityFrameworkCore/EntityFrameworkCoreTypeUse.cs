using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Frameworks.EntityFrameworkCore;

/// <summary>One source use of an EF Core type, with the bound identity that the EF rules need.</summary>
public sealed record EntityFrameworkCoreTypeUse
{
    /// <summary>Creates an immutable type-use snapshot.</summary>
    /// <param name="project">The policy-relative project containing the use.</param>
    /// <param name="user">The type whose source contains the use, such as a repository or handler.</param>
    /// <param name="symbol">The display name of the used EF Core type.</param>
    /// <param name="kind">What the use establishes about data access.</param>
    /// <param name="declaringProject">The policy-relative source project that declares the used type, or null for metadata types.</param>
    /// <param name="location">The use site, when available.</param>
    /// <param name="isOwnerAmbiguous">Whether several source projects produce the declaring assembly, so its owner is unknown.</param>
    /// <exception cref="ArgumentException">A required value is blank.</exception>
    public EntityFrameworkCoreTypeUse(string project, string user, string symbol, EntityFrameworkCoreTypeKind kind,
        string? declaringProject = null, SourceLocation? location = null, bool isOwnerAmbiguous = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Project = project;
        User = user;
        Symbol = symbol;
        Kind = kind;
        DeclaringProject = declaringProject;
        Location = location;
        IsOwnerAmbiguous = isOwnerAmbiguous;
    }

    /// <summary>Gets the policy-relative project containing the use.</summary>
    public string Project { get; }
    /// <summary>Gets the type whose source contains the use.</summary>
    public string User { get; }
    /// <summary>Gets the display name of the used EF Core type.</summary>
    public string Symbol { get; }
    /// <summary>Gets what the use establishes about data access.</summary>
    public EntityFrameworkCoreTypeKind Kind { get; }
    /// <summary>Gets the source project declaring the used type, or null for metadata types.</summary>
    public string? DeclaringProject { get; }
    /// <summary>Gets the use site, or null when unavailable.</summary>
    public SourceLocation? Location { get; }
    /// <summary>Gets whether several source projects produce the declaring assembly, so the type's owner cannot be resolved.</summary>
    public bool IsOwnerAmbiguous { get; }
}
