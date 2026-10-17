using ArchitectureGuardian.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace ArchitectureGuardian.Analysis.Workspaces;

/// <summary>Loads trusted C# solutions and projects using the installed SDK without performing restore.</summary>
/// <remarks>Design-time builds can execute repository code. This adapter does not provide a sandbox.</remarks>
public sealed class MSBuildWorkspaceLoader
{
    /// <summary>Loads the requested build context and validates compilation and reference coverage.</summary>
    /// <param name="request">The explicit input, trust decision, configuration and optional framework.</param>
    /// <param name="cancellationToken">Cooperative cancellation for workspace opening and compilation.</param>
    /// <returns>An owned workspace with completed, incomplete or cancelled loading coverage and actionable issues.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public async Task<WorkspaceLoadResult> LoadAsync(WorkspaceLoadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        MSBuildWorkspace? workspace = null;
        var issues = new List<AnalysisIssue>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!request.IsTrusted)
            {
                return new(null, AnalysisStatus.Incomplete,
                    [new("workspace-untrusted", "MSBuild loading can execute repository tasks. Explicitly trust the input or load it in an isolated environment.")]);
            }

            var extension = Path.GetExtension(request.InputPath).ToLowerInvariant();
            if (extension is not (".sln" or ".slnx" or ".csproj"))
            {
                return new(null, AnalysisStatus.Incomplete, [new("unsupported-input", "Choose a .sln, .slnx or .csproj file.")]);
            }

            if (!File.Exists(request.InputPath))
            {
                return new(null, AnalysisStatus.Incomplete, [new("input-unavailable", $"Input file does not exist: {request.InputPath}")]);
            }

            var workingDirectory = Path.GetDirectoryName(request.InputPath)!;
            var sdkVersion = await SdkDiscovery.ResolveAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
            MSBuildRegistration.EnsureRegistered(workingDirectory, sdkVersion);
            cancellationToken.ThrowIfCancellationRequested();
            var properties = new Dictionary<string, string> { ["Configuration"] = request.Configuration };
            if (request.TargetFramework is not null)
            {
                properties["TargetFramework"] = request.TargetFramework;
            }

            workspace = MSBuildWorkspace.Create(properties);
            workspace.SkipUnrecognizedProjects = false;
            workspace.LoadMetadataForReferencedProjects = false;
            if (extension == ".csproj")
            {
                await workspace.OpenProjectAsync(request.InputPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await workspace.OpenSolutionAsync(request.InputPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            if (workspace.CurrentSolution.ProjectIds.Count == 0)
            {
                issues.Add(new("no-projects", "The input contains no loadable C# projects."));
            }

            foreach (var project in workspace.CurrentSolution.Projects.OrderBy(project => project.FilePath, StringComparer.Ordinal)
                .ThenBy(project => project.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (project.Language != LanguageNames.CSharp || project.FilePath is null)
                {
                    issues.Add(new("unsupported-project", $"Project '{project.Name}' has no supported C# project context."));
                    continue;
                }

                var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                if (compilation is null)
                {
                    issues.Add(new("compilation-unavailable", $"{project.FilePath}: no compilation is available."));
                    continue;
                }

                WorkspaceProjectValidator.Validate(project.FilePath, request, compilation, issues);
                foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    issues.Add(new("compilation-error", $"{project.FilePath}: {diagnostic.ToString()}"));
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            AddWorkspaceIssues(workspace, issues);
            return new(workspace, issues.Count == 0 ? AnalysisStatus.Completed : AnalysisStatus.Incomplete, issues);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AddWorkspaceIssues(workspace, issues);
            return new(workspace, AnalysisStatus.Cancelled, issues);
        }
        catch (Exception exception)
        {
            // Preserve partial projects and diagnostics; a loading exception never establishes clean coverage.
            AddWorkspaceIssues(workspace, issues);
            issues.Add(new("workspace-load-failed", $"{request.InputPath}: {exception.GetType().Name}: {exception.Message}"));
            return new(workspace, AnalysisStatus.Incomplete, issues);
        }
    }

    private static void AddWorkspaceIssues(MSBuildWorkspace? workspace, List<AnalysisIssue> issues)
    {
        if (workspace is not null)
        {
            issues.AddRange(workspace.Diagnostics.Select(diagnostic =>
                new AnalysisIssue("workspace-diagnostic", $"{diagnostic.Kind}: {diagnostic.Message}")));
        }
    }
}
