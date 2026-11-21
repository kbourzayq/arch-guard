using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Frameworks.Abp;

/// <summary>One ABP module class declared in solution source, with every module its declarations make ABP load.</summary>
public sealed record AbpModuleClass
{
    /// <summary>Creates an immutable module snapshot.</summary>
    /// <param name="project">The policy-relative project that declares the module.</param>
    /// <param name="module">The display name of the module class.</param>
    /// <param name="dependsOn">Modules reachable through [DependsOn], directly or transitively, excluding the module itself.</param>
    /// <param name="location">The module declaration, when available.</param>
    /// <exception cref="ArgumentException">A required value is blank, or a dependency is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="dependsOn"/> is null.</exception>
    public AbpModuleClass(string project, string module, IEnumerable<string> dependsOn, SourceLocation? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentNullException.ThrowIfNull(dependsOn);
        var modules = dependsOn.ToArray();
        if (modules.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Module dependencies cannot be blank.", nameof(dependsOn));
        }

        Project = project;
        Module = module;
        DependsOn = Array.AsReadOnly(modules.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        Location = location;
    }

    /// <summary>Gets the policy-relative project that declares the module.</summary>
    public string Project { get; }
    /// <summary>Gets the display name of the module class.</summary>
    public string Module { get; }
    /// <summary>Gets the transitive [DependsOn] closure in ordinal order.</summary>
    public IReadOnlyList<string> DependsOn { get; }
    /// <summary>Gets the module declaration, or null when unavailable.</summary>
    public SourceLocation? Location { get; }
}
