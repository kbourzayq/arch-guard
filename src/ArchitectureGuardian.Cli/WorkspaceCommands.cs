using ArchitectureGuardian.Analysis.Baselines;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Repositories;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Baselines;

namespace ArchitectureGuardian.Cli;

/// <summary>Composes shared policy, workspace and analysis services without implementing rule semantics.</summary>
internal static class WorkspaceCommands
{
    internal static async Task<int> RunAsync(string command, WorkspaceLoadRequest request, string? policyPath,
        string? baselinePath, ConsoleRenderer renderer, CancellationToken cancellationToken)
    {
        var fullPolicyPath = policyPath is null ? null : Path.GetFullPath(policyPath);
        var policy = fullPolicyPath is null ? null : await new YamlPolicyLoader().LoadAsync(fullPolicyPath, cancellationToken);
        if (baselinePath is not null) { ArgumentException.ThrowIfNullOrWhiteSpace(baselinePath); }
        if (command is "scan" or "update")
        {
            var store = new JsonBaselineStore();
            var baseline = command == "scan" && baselinePath is not null
                ? await store.LoadAsync(baselinePath, cancellationToken) : null;
            var analysis = await new SolutionAnalysisService().AnalyzeAsync(request, Path.GetDirectoryName(fullPolicyPath!)!, policy!, cancellationToken);
            renderer.Diagnostics(analysis.Diagnostics);
            if (!analysis.IsComplete)
            {
                return renderer.Coverage("Analysis", analysis.Status, analysis.Issues);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (command == "update")
            {
                var snapshot = BaselineSnapshot.Capture(analysis);
                await store.SaveAsync(baselinePath!, snapshot, cancellationToken);
                renderer.BaselineUpdated(snapshot.Entries.Count);
                return 0;
            }

            var comparison = baseline is null ? null : new BaselineComparison(analysis, baseline);
            if (comparison is not null) { renderer.Baseline(comparison); }
            var passed = comparison is null ? policy!.Gate.Passes(analysis) : policy!.Gate.PassesNewFindings(comparison);
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
