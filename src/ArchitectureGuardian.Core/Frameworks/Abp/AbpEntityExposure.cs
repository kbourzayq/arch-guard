using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Frameworks.Abp;

/// <summary>One application service member whose signature exposes an ABP entity, directly or through a DTO property.</summary>
public sealed record AbpEntityExposure
{
    /// <summary>Creates an immutable exposure snapshot.</summary>
    /// <param name="project">The policy-relative project that declares the application service.</param>
    /// <param name="service">The display name of the application service class or interface.</param>
    /// <param name="member">The exposing member, such as GetAsync, or a DTO property path such as GetSummaryAsync → OrderSummaryDto.Source.</param>
    /// <param name="entity">The display name of the exposed entity type.</param>
    /// <param name="location">The member or DTO property declaration, when available.</param>
    /// <exception cref="ArgumentException">A required value is blank.</exception>
    public AbpEntityExposure(string project, string service, string member, string entity, SourceLocation? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        Project = project;
        Service = service;
        Member = member;
        Entity = entity;
        Location = location;
    }

    /// <summary>Gets the policy-relative project that declares the application service.</summary>
    public string Project { get; }
    /// <summary>Gets the application service display name.</summary>
    public string Service { get; }
    /// <summary>Gets the exposing member or DTO property path.</summary>
    public string Member { get; }
    /// <summary>Gets the exposed entity display name.</summary>
    public string Entity { get; }
    /// <summary>Gets the declaration site, or null when unavailable.</summary>
    public SourceLocation? Location { get; }
}
