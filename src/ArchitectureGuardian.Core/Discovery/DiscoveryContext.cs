namespace ArchitectureGuardian.Core.Discovery;

/// <summary>Records one evaluated project context; declared frameworks are choices, not explored contexts.</summary>
public sealed class DiscoveryContext
{
    /// <summary>Snapshots an evaluated configuration/framework using a portable project identity.</summary>
    /// <param name="project">The discovery-root-relative project path.</param>
    /// <param name="configuration">Evaluated build configuration.</param>
    /// <param name="framework">Evaluated inner-build framework.</param>
    /// <param name="declaredFrameworks">Framework choices declared in this configuration.</param>
    public DiscoveryContext(string project, string configuration, string framework, IEnumerable<string> declaredFrameworks)
    {
        Project = project;
        Configuration = configuration;
        Framework = framework;
        DeclaredFrameworks = Array.AsReadOnly(declaredFrameworks.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Gets the portable project identity.</summary>
    public string Project { get; }
    /// <summary>Gets the actual evaluated configuration.</summary>
    public string Configuration { get; }
    /// <summary>Gets the actual evaluated framework.</summary>
    public string Framework { get; }
    /// <summary>Gets declared choices, including frameworks not explored.</summary>
    public IReadOnlyList<string> DeclaredFrameworks { get; }
}
