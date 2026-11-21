using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Frameworks.Abp;

/// <summary>One source use of an ABP repository type, including custom repository interfaces and implementations.</summary>
public sealed record AbpRepositoryUse
{
    /// <summary>Creates an immutable repository-use snapshot.</summary>
    /// <param name="project">The policy-relative project containing the use.</param>
    /// <param name="user">The type whose source contains the use.</param>
    /// <param name="repository">The display name of the used repository type.</param>
    /// <param name="location">The use site, when available.</param>
    /// <exception cref="ArgumentException">A required value is blank.</exception>
    public AbpRepositoryUse(string project, string user, string repository, SourceLocation? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        Project = project;
        User = user;
        Repository = repository;
        Location = location;
    }

    /// <summary>Gets the policy-relative project containing the use.</summary>
    public string Project { get; }
    /// <summary>Gets the type whose source contains the use.</summary>
    public string User { get; }
    /// <summary>Gets the repository display name.</summary>
    public string Repository { get; }
    /// <summary>Gets the use site, or null when unavailable.</summary>
    public SourceLocation? Location { get; }
}
