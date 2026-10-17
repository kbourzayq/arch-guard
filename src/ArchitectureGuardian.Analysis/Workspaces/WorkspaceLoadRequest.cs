namespace ArchitectureGuardian.Analysis.Workspaces;

/// <summary>Defines a trusted workspace input and the build context to evaluate.</summary>
public sealed class WorkspaceLoadRequest
{
    /// <summary>Creates a request; repository build logic is allowed only with explicit trust.</summary>
    /// <param name="inputPath">A solution or C# project file.</param>
    /// <param name="isTrusted">Whether the caller authorizes execution of repository build logic.</param>
    /// <param name="configuration">The build configuration, defaulting to Debug.</param>
    /// <param name="targetFramework">An explicit target framework, required for multi-targeted projects.</param>
    /// <exception cref="ArgumentException">A required value or supplied framework is blank.</exception>
    public WorkspaceLoadRequest(string inputPath, bool isTrusted = false, string configuration = "Debug",
        string? targetFramework = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);
        if (targetFramework is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        }

        InputPath = Path.GetFullPath(inputPath);
        IsTrusted = isTrusted;
        Configuration = configuration;
        TargetFramework = targetFramework;
    }

    /// <summary>Gets the absolute input file path.</summary>
    public string InputPath { get; }

    /// <summary>Gets whether repository build logic may execute.</summary>
    public bool IsTrusted { get; }

    /// <summary>Gets the configuration controlling conditional items and compilation symbols.</summary>
    public string Configuration { get; }

    /// <summary>Gets the requested framework, or null for projects declaring a single framework.</summary>
    public string? TargetFramework { get; }
}
