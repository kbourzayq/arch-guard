using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Repositories;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;

namespace ArchitectureGuardian.Cli;

/// <summary>Composes shared policy, workspace and analysis services without implementing rule semantics.</summary>
internal static class WorkspaceCommands
{
    internal static async Task<int> RunAsync(string command, WorkspaceLoadRequest request, string? policyPath,
        ConsoleRenderer renderer, CancellationToken cancellationToken)
    {
        var fullPolicyPath = policyPath is null ? null : Path.GetFullPath(policyPath);
        var policy = fullPolicyPath is null ? null : await new YamlPolicyLoader().LoadAsync(fullPolicyPath, cancellationToken);
        if (command == "scan")
        {
            var analysis = await new SolutionAnalysisService().AnalyzeAsync(request, Path.GetDirectoryName(fullPolicyPath!)!, policy!, cancellationToken);
            renderer.Diagnostics(analysis.Diagnostics);
            if (!analysis.IsComplete)
            {
                return renderer.Coverage("Analysis", analysis.Status, analysis.Issues);
            }

            var passed = policy!.Gate.Passes(analysis);
            renderer.Gate(passed, analysis.Diagnostics.Count);
            return passed ? 0 : 1;
        }

        using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(request, cancellationToken);
        if (command == "load")
        {
            if (workspace.Status != AnalysisStatus.Completed)
            {
                return renderer.Coverage("Workspace loading", workspace.Status, workspace.Issues);
            }

            renderer.Loaded(workspace.Solution!.ProjectIds.Count, request.Configuration);
            return 0;
        }

        var policyDirectory = Path.GetDirectoryName(fullPolicyPath!)!;
        if (command == "inspect")
        {
            var repository = await new RepositoryModelBuilder().BuildAsync(workspace, policyDirectory, policy!, cancellationToken);
            if (repository.Model is not null)
            {
                renderer.Inspect(repository.Model);
            }

            return renderer.Coverage("Inspection", repository.Status, repository.Issues);
        }

        throw new ArgumentException("Unknown workspace command.", nameof(command));
    }
}
