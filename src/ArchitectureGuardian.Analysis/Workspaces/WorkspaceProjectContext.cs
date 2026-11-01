namespace ArchitectureGuardian.Analysis.Workspaces;

/// <summary>Records the actual evaluated inner build and the frameworks declared by its outer build.</summary>
public sealed class WorkspaceProjectContext
{
    /// <summary>Snapshots evaluated values and declared choices without retaining mutable build objects.</summary>
    /// <param name="projectPath">Absolute project path in the workspace adapter.</param>
    /// <param name="configuration">Evaluated configuration.</param>
    /// <param name="targetFramework">Evaluated inner-build framework.</param>
    /// <param name="declaredFrameworks">Declared choices, not a claim that all were explored.</param>
    public WorkspaceProjectContext(string projectPath, string configuration, string targetFramework, IEnumerable<string> declaredFrameworks)
    {
        ProjectPath = projectPath;
        Configuration = configuration;
        TargetFramework = targetFramework;
        DeclaredFrameworks = Array.AsReadOnly(declaredFrameworks.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Gets the absolute project path.</summary>
    public string ProjectPath { get; }
    /// <summary>Gets the evaluated configuration.</summary>
    public string Configuration { get; }
    /// <summary>Gets the evaluated inner-build framework.</summary>
    public string TargetFramework { get; }
    /// <summary>Gets immutable declared choices, independently of explored coverage.</summary>
    public IReadOnlyList<string> DeclaredFrameworks { get; }
}
