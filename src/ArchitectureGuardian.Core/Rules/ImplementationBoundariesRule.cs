using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Rules;

/// <summary>Checks AG004 semantic access to implementation projects across declared module boundaries.</summary>
/// <remarks>Public contracts and same-module uses pass independently of AG001 permissions and C# accessibility.</remarks>
public sealed class ImplementationBoundariesRule : IArchitectureRule<DependencyBuildResult>
{
    /// <inheritdoc/>
    public RuleDescriptor Descriptor => RuleCatalog.ImplementationBoundaries;

    /// <summary>Groups semantic evidence by original project pair and target symbol, ignoring unused references.</summary>
    /// <param name="context">The repository, semantic graph and upstream coverage for this scope.</param>
    /// <param name="cancellationToken">Cooperative cancellation during evidence evaluation.</param>
    /// <returns>Severity-free findings preserving upstream coverage and unmapped-project issues.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupts evaluation.</exception>
    public Task<RuleEvaluationResult> EvaluateAsync(DependencyBuildResult context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Repository is null || context.Graph is null)
        {
            return Task.FromResult(new RuleEvaluationResult(context.Status, issues: context.Issues));
        }

        var policy = context.Repository.ResolvedPolicy;
        var issues = context.Issues.ToHashSet();
        var violations = new List<RuleViolation>();
        var uses = new Dictionary<(string Source, string Target, string Symbol), HashSet<DiagnosticEvidence>>();
        foreach (var edge in context.Graph.Edges)
        {
            foreach (var evidence in edge.Evidence)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (evidence.TargetProject is not { } target || evidence.TargetSymbol is not { } symbol)
                {
                    continue;
                }

                var key = (evidence.SourceProject, target, symbol);
                if (!uses.TryGetValue(key, out var facts))
                {
                    facts = [];
                    uses.Add(key, facts);
                }

                facts.Add(evidence.Fact);
            }
        }

        foreach (var (use, facts) in uses.OrderBy(item => item.Key.Source, StringComparer.Ordinal)
            .ThenBy(item => item.Key.Target, StringComparer.Ordinal).ThenBy(item => item.Key.Symbol, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!policy.Projects.TryGetValue(use.Source, out var source) || !policy.Projects.TryGetValue(use.Target, out var target))
            {
                issues.Add(new("dependency-project-unmapped",
                    $"Dependency '{use.Source}' → '{use.Target}' is outside the supplied repository's policy inventory.", Descriptor.Id));
                continue;
            }

            if (DependencySemantics.AllowsImplementation(source.ModuleId, target.ModuleId, target.ContractId))
            {
                continue;
            }

            var evidence = facts.OrderBy(fact => fact.CanonicalKey, StringComparer.Ordinal).ToArray();
            violations.Add(new(Descriptor,
                $"Module '{source.ModuleId}' must not access implementation of module '{target.ModuleId}': project '{use.Source}' uses '{use.Symbol}' from '{use.Target}'.",
                new(module: source.ModuleId, symbol: use.Symbol), evidence,
                $"Use a declared public contract of module '{target.ModuleId}' or move the interaction inside that module. C# public accessibility and module permissions do not expose implementation as a contract.",
                evidence.FirstOrDefault(fact => fact.Location is not null)?.Location,
                new ViolationContext([new("project", use.Source), new("project", use.Target), new("symbol", use.Symbol)],
                    [new("modules." + target.ModuleId, "contracts",
                        context.Repository.Policy.Modules.Single(module => module.Id == target.ModuleId).Contracts.Select(contract => contract.Id))],
                    context.Repository.Policy.Modules.Single(module => module.Id == target.ModuleId).Contracts
                        .Select(contract => $"contract:{target.ModuleId}/{contract.Id}"))));
        }

        var status = context.Status == AnalysisStatus.Completed && issues.Count > 0 ? AnalysisStatus.Incomplete : context.Status;
        return Task.FromResult(new RuleEvaluationResult(status, violations, issues));
    }
}
