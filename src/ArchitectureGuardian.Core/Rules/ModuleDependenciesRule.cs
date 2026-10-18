using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Rules;

/// <summary>Checks AG001 direct module permissions using the graph's original project pairs.</summary>
/// <remarks>Policy selection controls whether this rule runs; severity and gates are applied after evaluation.</remarks>
public sealed class ModuleDependenciesRule : IArchitectureRule<DependencyBuildResult>
{
    /// <inheritdoc/>
    public RuleDescriptor Descriptor => RuleCatalog.ModuleDependencies;

    /// <summary>Reports one finding per forbidden project pair, retaining distinct reference and semantic facts.</summary>
    /// <param name="context">The repository, evidence graph and coverage for the supplied analysis scope.</param>
    /// <param name="cancellationToken">Cooperative cancellation observed while inspecting dependency evidence.</param>
    /// <returns>Severity-free findings with extraction coverage and any unmapped-project issues preserved.</returns>
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
        var issues = context.Issues.ToList();
        var violations = new List<RuleViolation>();
        var pairs = new Dictionary<(string Source, string Target), HashSet<DiagnosticEvidence>>();
        // One observed use can appear at several graph levels, including logical aggregates.
        // Keep project pairs intact so a contract permission never authorizes implementation access.
        foreach (var edge in context.Graph.Edges)
        {
            foreach (var evidence in edge.Evidence)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (evidence.TargetProject is not { } target)
                {
                    continue;
                }

                var pair = (evidence.SourceProject, target);
                if (!pairs.TryGetValue(pair, out var facts))
                {
                    facts = [];
                    pairs.Add(pair, facts);
                }

                facts.Add(evidence.Fact);
            }
        }

        foreach (var (pair, facts) in pairs.OrderBy(item => item.Key.Source, StringComparer.Ordinal)
            .ThenBy(item => item.Key.Target, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!policy.Projects.TryGetValue(pair.Source, out var source) || !policy.Projects.TryGetValue(pair.Target, out var target))
            {
                issues.Add(new("dependency-project-unmapped",
                    $"Dependency '{pair.Source}' → '{pair.Target}' is outside the supplied repository's policy inventory.", Descriptor.Id));
                continue;
            }

            if (policy.AllowsModuleDependency(pair.Source, pair.Target))
            {
                continue;
            }

            var evidence = facts.OrderBy(fact => fact.CanonicalKey, StringComparer.Ordinal).ToArray();
            violations.Add(new(Descriptor,
                $"Module '{source.ModuleId}' must not depend on module '{target.ModuleId}': project '{pair.Source}' depends on '{pair.Target}'.",
                new(module: source.ModuleId), evidence,
                $"Remove the dependency or use a public contract of module '{target.ModuleId}' explicitly permitted by module '{source.ModuleId}'. If intended, declare the exact module or contract permission in allowedDependencies.",
                evidence.FirstOrDefault(fact => fact.Location is not null)?.Location));
        }

        var status = context.Status == AnalysisStatus.Completed && issues.Count > 0 ? AnalysisStatus.Incomplete : context.Status;
        return Task.FromResult(new RuleEvaluationResult(status, violations, issues));
    }
}
