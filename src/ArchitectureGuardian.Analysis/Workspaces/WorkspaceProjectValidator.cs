using System.Runtime.CompilerServices;
using ArchitectureGuardian.Core.Analysis;
using Microsoft.Build.Evaluation;

namespace ArchitectureGuardian.Analysis.Workspaces;

/// <summary>Checks evaluated framework selection and restore prerequisites without invoking restore.</summary>
internal static class WorkspaceProjectValidator
{
    // Keep MSBuild-dependent JIT compilation behind SDK registration.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Validate(string projectPath, WorkspaceLoadRequest request, IReadOnlyDictionary<string, string> workspaceProperties,
        Microsoft.CodeAnalysis.Compilation compilation, List<AnalysisIssue> issues)
    {
        // MSBuildWorkspace adds solution globals during opening; retain them along with caller properties.
        var properties = new Dictionary<string, string>(workspaceProperties, StringComparer.OrdinalIgnoreCase);
        // Match the design-time defaults of the pinned Roslyn 5.0 build host on .NET.
        foreach (var (name, value) in new Dictionary<string, string>
        {
            ["DesignTimeBuild"] = bool.TrueString,
            ["NonExistentFile"] = "__NonExistentSubDir__\\__NonExistentFile__",
            ["BuildProjectReferences"] = bool.FalseString,
            ["BuildingProject"] = bool.FalseString,
            ["ProvideCommandLineArgs"] = bool.TrueString,
            ["SkipCompilerExecution"] = bool.TrueString,
            ["ContinueOnError"] = "ErrorAndContinue",
            ["ShouldUnsetParentConfigurationAndPlatform"] = bool.FalseString,
        })
        {
            properties.TryAdd(name, value);
        }

        // Inspect declarations without forcing a framework, then evaluate the selected inner build below.
        properties.Remove("TargetFramework");
        using var projects = new ProjectCollection(properties);
        var project = projects.LoadProject(projectPath);
        var frameworks = project.GetPropertyValue("TargetFrameworks").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (frameworks.Length == 0)
        {
            frameworks = [project.GetPropertyValue("TargetFramework")];
        }

        if (request.TargetFramework is null && frameworks.Length > 1)
        {
            issues.Add(new("framework-required", $"{projectPath}: select a target framework explicitly ({string.Join(", ", frameworks)}). Run each framework separately."));
        }
        else if (request.TargetFramework is not null && !frameworks.Contains(request.TargetFramework, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new("framework-unavailable", $"{projectPath}: target framework '{request.TargetFramework}' is not declared ({string.Join(", ", frameworks)})."));
        }

        // Evaluate the inner build so framework-specific imports and asset paths are honored.
        project.SetGlobalProperty("TargetFramework", request.TargetFramework ?? frameworks[0]);
        project.ReevaluateIfNecessary();
        var assetsPath = project.GetPropertyValue("ProjectAssetsFile");
        if (project.GetPropertyValue("UsingMicrosoftNETSdk") == "true" &&
            (string.IsNullOrWhiteSpace(assetsPath) || !File.Exists(Path.GetFullPath(assetsPath, Path.GetDirectoryName(projectPath)!))))
        {
            issues.Add(new("restore-required", $"{projectPath}: restore assets are unavailable. Run dotnet restore successfully in the trusted environment, then retry."));
        }

        var resolvedNames = compilation.References.Select(reference => compilation.GetAssemblyOrModuleSymbol(reference)?.Name)
            .Where(name => name is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resolvedPaths = compilation.References.OfType<Microsoft.CodeAnalysis.PortableExecutableReference>()
            .Select(reference => reference.FilePath).Where(path => path is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in project.GetItems("Reference"))
        {
            var identity = reference.EvaluatedInclude.Split(',')[0].Trim();
            var name = Path.GetFileName(identity);
            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                name = Path.GetFileNameWithoutExtension(name);
            }

            var hint = reference.GetMetadataValue("HintPath");
            var path = Path.GetFullPath(string.IsNullOrWhiteSpace(hint) ? identity : hint, Path.GetDirectoryName(projectPath)!);
            // Design-time builds can silently drop unused unresolved references; compilation errors alone miss them.
            if (!resolvedNames.Contains(name) && !resolvedPaths.Contains(path))
            {
                issues.Add(new("reference-unavailable", $"{projectPath}: assembly reference '{reference.EvaluatedInclude}' is unavailable in the compilation."));
            }
        }
    }
}
