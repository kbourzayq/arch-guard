namespace ArchitectureGuardian.Core.Frameworks.AspNetCore;

/// <summary>The lifetime a registration declares with the Microsoft dependency injection container.</summary>
public enum AspNetCoreServiceLifetime
{
    /// <summary>One instance per container.</summary>
    Singleton,
    /// <summary>One instance per scope, typically per request.</summary>
    Scoped,
    /// <summary>A new instance per resolution.</summary>
    Transient,
}
