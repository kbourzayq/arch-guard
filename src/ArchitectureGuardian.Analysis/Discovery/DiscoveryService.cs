using ArchitectureGuardian.Analysis.Dependencies;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Repositories;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Discovery;
using ArchitectureGuardian.Core.Repositories;

namespace ArchitectureGuardian.Analysis.Discovery;

/// <summary>Discovers facts before proposing intentions; never writes or activates policy.</summary>
public sealed class DiscoveryService
{
    /// <summary>Explores exactly one authorized configuration/framework through the existing workspace and semantic services.</summary>
    /// <param name="request">Input, build context and explicit trust authorization.</param>
    /// <param name="root">Existing directory above every input/project; future policy identities are relative to it.</param>
    /// <param name="cancellationToken">Cooperative cancellation; available evidence retains noncompleted coverage.</param>
    /// <returns>Reusable observations, inferences, contexts and independent candidate compliance.</returns>
    public async Task<DiscoveryResult> DiscoverAsync(WorkspaceLoadRequest request, string root, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        root = Path.GetFullPath(root);
        var input = Path.GetRelativePath(root, request.InputPath).Replace('\\', '/');
        IReadOnlyList<string> staticProjects = [];
        var staticIssues = new List<AnalysisIssue>();
        var contexts = new List<DiscoveryContext>();
        DependencyInventoryResult? observations = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            input = RepositoryModelBuilder.RelativePath(root, request.InputPath);
            // Static XML/path inspection is deliberately separate from evaluated identities and dependencies.
            staticProjects = DiscoveryInputs.LiteralProjects(request.InputPath, root, staticIssues, cancellationToken);
            using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(request, cancellationToken).ConfigureAwait(false);
            foreach (var context in workspace.Contexts)
            {
                contexts.Add(new(Path.GetRelativePath(root, context.ProjectPath).Replace('\\', '/'), context.Configuration,
                    context.TargetFramework, context.DeclaredFrameworks));
            }
            observations = await new DependencyGraphBuilder().BuildInventoryAsync(workspace, root, cancellationToken).ConfigureAwait(false);
            if (observations.Status == AnalysisStatus.Cancelled) { return Result(new PolicyProposalBuilder().Build(observations)); }
            var proposal = new PolicyProposalBuilder().Build(observations, cancellationToken);
            AnalysisResult? compliance = null;
            if (proposal.Policy is { } policy && observations.Inventory is { } inventory && observations.Graph is { } graph)
            {
                var repository = new RepositoryModel(inventory.Id, policy, inventory.Projects);
                var context = new DependencyBuildResult(repository, graph.Aggregate(repository), observations.Status, observations.Issues);
                compliance = await new AnalysisService<DependencyBuildResult>(policy.SelectRules(SolutionRules.All)).AnalyzeAsync(
                    new(context, policy.SeverityPolicy), cancellationToken).ConfigureAwait(false);
            }
            return Result(proposal, compliance);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            observations = new(observations?.Inventory, observations?.Graph, AnalysisStatus.Cancelled, observations?.Issues);
            return Result(new PolicyProposalBuilder().Build(observations));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Xml.XmlException)
        {
            observations = new(observations?.Inventory, observations?.Graph, AnalysisStatus.Failed,
                (observations?.Issues ?? []).Append(new AnalysisIssue("discovery-input-failed", exception.Message)));
            return Result(new PolicyProposalBuilder().Build(observations));
        }

        DiscoveryResult Result(PolicyProposal proposal, AnalysisResult? compliance = null) =>
            new(input, request.Configuration, request.TargetFramework, staticProjects, contexts, observations!, proposal, compliance, staticIssues);
    }
}
