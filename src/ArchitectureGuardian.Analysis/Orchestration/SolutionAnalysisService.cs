using ArchitectureGuardian.Analysis.Changes;
using ArchitectureGuardian.Analysis.Dependencies;
using ArchitectureGuardian.Analysis.Telemetry;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Analysis.Orchestration;

/// <summary>Shares full solution scans and full-context Git comparison between hosts.</summary>
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
        => await AnalysisTelemetry.MeasureAsync("analysis", () => AnalyzeCoreAsync(request, policyDirectory, policy, cancellationToken),
            result => result.Status, cancellationToken).ConfigureAwait(false);

    private async Task<AnalysisResult> AnalyzeCoreAsync(WorkspaceLoadRequest request, string policyDirectory, ArchitecturePolicy policy, CancellationToken cancellationToken)
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

    /// <summary>Compares complete base and working-tree scans using each snapshot's policy and stable finding fingerprints.</summary>
    /// <param name="request">Candidate input, trust for both snapshots and shared build context.</param>
    /// <param name="policyPath">Explicit policy path, present at the same relative path in both snapshots.</param>
    /// <param name="baseReference">Exact Git commit/ref; no implicit merge-base selection or network fetch.</param>
    /// <param name="loader">Host-supplied policy loader, invoked separately for both snapshots.</param>
    /// <param name="allowedRoot">Optional operator boundary that must contain the Git root.</param>
    /// <param name="cancellationToken">Cooperative cancellation through snapshot capture, offline preparation and scans.</param>
    /// <returns>Candidate findings and combined coverage; only completed comparisons can pass a new-finding gate.</returns>
    /// <remarks>Comparison build-context values accept only letters, digits, spaces, '.', '_' and '-'; unsupported MSBuild syntax returns incomplete coverage before preparation.</remarks>
    public Task<ChangeAnalysisResult> AnalyzeChangesAsync(WorkspaceLoadRequest request, string policyPath,
        string baseReference, IPolicyLoader loader, string? allowedRoot = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseReference);
        ArgumentNullException.ThrowIfNull(loader);
        return AnalysisTelemetry.MeasureAsync("changes",
            () => ChangeAnalysisService.AnalyzeAsync(request, policyPath, baseReference, loader, allowedRoot, cancellationToken),
            result => result.Analysis.Status, cancellationToken);
    }
}
