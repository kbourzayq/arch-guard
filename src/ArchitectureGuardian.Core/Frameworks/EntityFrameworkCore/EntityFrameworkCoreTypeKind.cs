namespace ArchitectureGuardian.Core.Frameworks.EntityFrameworkCore;

/// <summary>Classifies an EF Core type by what a use of it establishes about data access.</summary>
public enum EntityFrameworkCoreTypeKind
{
    /// <summary>An EF Core type other than a DbContext, such as DbSet, an extension class or a configuration interface.</summary>
    Persistence,
    /// <summary>A concrete DbContext declared in source, whose owner is the module of its declaring project.</summary>
    DbContext,
    /// <summary>The framework DbContext base type, which hides which context is supplied at runtime.</summary>
    BaseDbContext,
}
