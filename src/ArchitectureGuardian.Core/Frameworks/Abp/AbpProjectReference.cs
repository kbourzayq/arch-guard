namespace ArchitectureGuardian.Core.Frameworks.Abp;

/// <summary>A direct reference from one solution project to another, which ABP module declarations must mirror.</summary>
public sealed record AbpProjectReference
{
    /// <summary>Creates an immutable project-reference snapshot.</summary>
    /// <param name="project">The policy-relative referencing project.</param>
    /// <param name="referencedProject">The policy-relative referenced project.</param>
    /// <exception cref="ArgumentException">A value is blank.</exception>
    public AbpProjectReference(string project, string referencedProject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(referencedProject);
        Project = project;
        ReferencedProject = referencedProject;
    }

    /// <summary>Gets the policy-relative referencing project.</summary>
    public string Project { get; }
    /// <summary>Gets the policy-relative referenced project.</summary>
    public string ReferencedProject { get; }
}
