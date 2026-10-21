using ArchitectureGuardian.Analysis.Dependencies;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Analysis.Orchestration;

/// <summary>Shares the solution scan pipeline and reserved change-analysis entry point between hosts.</summary>
public sealed class SolutionAnalysisService
{
    /// <summary>Selects policy rules, loads trusted context, extracts dependencies and evaluates the shared solution engine.</summary>
    /// <param name="request">Input, operator trust and build context.</param>
    /// <param name="policyDirectory">Root for policy-relative project and source identities.</param>
    /// <param name="policy">Validated policy, including selected rules and reporting severities.</param>
    /// <param name="cancellationToken">Cooperative cancellation forwarded through all stages.</param>
    /// <returns>Coverage, evidence-backed diagnostics and operational issues; cancellation retains shared coverage semantics.</returns>
    public async Task<AnalysisResult> AnalyzeAsync(WorkspaceLoadRequest request, string policyDirectory,
        ArchitecturePolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyDirectory);
        ArgumentNullException.ThrowIfNull(policy);
        if (cancellationToken.IsCancellationRequested) { return new(AnalysisStatus.Cancelled); }
        // Reject unavailable selected implementations before executing repository build logic.
        var rules = policy.SelectRules(SolutionRules.All);
        using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(request, cancellationToken).ConfigureAwait(false);
        var context = await new DependencyGraphBuilder().BuildAsync(workspace, policyDirectory, policy, cancellationToken).ConfigureAwait(false);
        return await new AnalysisService<DependencyBuildResult>(rules).AnalyzeAsync(
            new(context, policy.SeverityPolicy), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reserves full-context base/candidate comparison without substituting a passing full scan.</summary>
    /// <param name="baseReference">Explicit base commit or reference; not resolved until F23 implements comparison.</param>
    /// <param name="scope">Explicit comparison scope; currently only solution is defined.</param>
    /// <param name="cancellationToken">Cooperative cancellation; no repository build or Git operation is performed.</param>
    /// <returns>An incomplete result explaining that comparison is unsupported, never a completed empty analysis.</returns>
    /// <exception cref="ArgumentException">The base is blank or the scope is not solution.</exception>
    /// <exception cref="OperationCanceledException">Cancellation is requested.</exception>
    public Task<AnalysisResult> AnalyzeChangesAsync(string baseReference, string scope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(baseReference);
        if (scope != "solution") { throw new ArgumentException("Only the solution comparison scope is defined.", nameof(scope)); }
        return Task.FromResult(new AnalysisResult(AnalysisStatus.Incomplete, issues:
            [new("change-analysis-unsupported", "Base/candidate graph comparison is unavailable until F23. No reference was resolved and no changes were analyzed.")]));
    }
}
