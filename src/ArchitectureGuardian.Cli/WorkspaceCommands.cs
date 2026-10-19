using ArchitectureGuardian.Analysis.Dependencies;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Repositories;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Cli;

/// <summary>Composes shared policy, workspace and analysis services without implementing rule semantics.</summary>
internal static class WorkspaceCommands
{
    internal static async Task<int> RunAsync(string command, WorkspaceLoadRequest request, string? policyPath,
        ConsoleRenderer renderer, CancellationToken cancellationToken)
    {
        var fullPolicyPath = policyPath is null ? null : Path.GetFullPath(policyPath);
        var policy = fullPolicyPath is null ? null : await new YamlPolicyLoader().LoadAsync(fullPolicyPath, cancellationToken);
        // Selection fails before executing repository build logic when the engine lacks a requested rule.
        var rules = command == "scan" ? policy!.SelectRules<DependencyBuildResult>(
            [new ModuleDependenciesRule(), new LayerDependenciesRule(), new ModuleCyclesRule(), new ImplementationBoundariesRule(), new ForbiddenReferencesRule(), new DomainInfrastructureRule()]) : null;
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

        var context = await new DependencyGraphBuilder().BuildAsync(workspace, policyDirectory, policy!, cancellationToken);
        var analysis = await new AnalysisService<DependencyBuildResult>(rules!).AnalyzeAsync(
            new AnalysisRequest<DependencyBuildResult>(context, policy!.SeverityPolicy), cancellationToken);
        renderer.Diagnostics(analysis.Diagnostics);
        if (!analysis.IsComplete)
        {
            return renderer.Coverage("Analysis", analysis.Status, analysis.Issues);
        }

        var passed = policy.Gate.Passes(analysis);
        renderer.Gate(passed, analysis.Diagnostics.Count);
        return passed ? 0 : 1;
    }
}
