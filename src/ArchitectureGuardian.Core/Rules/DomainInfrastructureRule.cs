using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Rules;

/// <summary>Checks AG006 technology uses in explicitly configured source layers, retaining semantic evidence.</summary>
public sealed class DomainInfrastructureRule : IArchitectureRule<DependencyBuildResult>
{
    /// <inheritdoc/>
    public RuleDescriptor Descriptor => RuleCatalog.DomainInfrastructure;

    /// <summary>Groups semantic uses by project, symbol and violated layer technology restriction.</summary>
    /// <param name="context">The repository, bound reference evidence and upstream coverage.</param>
    /// <param name="cancellationToken">Cooperative cancellation during evaluation.</param>
    /// <returns>Severity-free findings retaining source evidence and coverage issues.</returns>
    public Task<RuleEvaluationResult> EvaluateAsync(DependencyBuildResult context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Repository is null || context.Graph is null)
        {
            return Task.FromResult(new RuleEvaluationResult(context.Status, issues: context.Issues));
        }

        var repository = context.Repository;
        var layers = repository.Policy.Layers.ToDictionary(layer => layer.Id, StringComparer.Ordinal);
        var issues = context.Issues.ToHashSet();
        var uses = new Dictionary<(string Project, string Symbol, string Scope, string Kind, string Name), HashSet<DiagnosticEvidence>>();
        foreach (var evidence in context.Graph.Edges.SelectMany(edge => edge.Evidence).Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (evidence.TargetSymbol is not { } symbol)
            {
                continue;
            }

            if (!repository.ResolvedPolicy.Projects.TryGetValue(evidence.SourceProject, out var source))
            {
                issues.Add(new("dependency-project-unmapped", $"Project '{evidence.SourceProject}' has no policy ownership.", Descriptor.Id));
                continue;
            }

            var restrictions = layers[source.LayerId].DomainInfrastructure;
            if (restrictions is null || restrictions.ForbiddenAssemblies.Count + restrictions.ForbiddenNamespaces.Count == 0)
            {
                continue;
            }

            if (evidence.TargetAssembly is null || evidence.TargetNamespace is null)
            {
                issues.Add(new("dependency-reference-identity-unavailable",
                    $"Bound assembly/namespace identity for '{symbol}' in '{evidence.SourceProject}' is unavailable.", Descriptor.Id,
                    evidence.Fact.Location));
                continue;
            }

            var scope = "layers." + source.LayerId + ".domainInfrastructure";
            foreach (var (kind, name) in DomainInfrastructureSemantics.Restrictions(evidence.TargetAssembly, evidence.TargetNamespace,
                restrictions.ForbiddenAssemblies, restrictions.ForbiddenNamespaces, restrictions.AllowedNamespaces))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = (evidence.SourceProject, symbol, scope, kind, name);
                if (!uses.TryGetValue(key, out var facts)) { uses.Add(key, facts = []); }
                facts.Add(evidence.Fact);
            }
        }

        var violations = uses.Select(use =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (project, symbol, scope, kind, name) = use.Key;
            var evidence = use.Value.OrderBy(fact => fact.CanonicalKey, StringComparer.Ordinal).ToArray();
            return new RuleViolation(Descriptor,
                $"Project '{project}' uses '{symbol}', violating {scope}.{kind} restriction '{name}'.",
                new(module: repository.ResolvedPolicy.Projects[project].ModuleId, symbol: symbol), evidence,
                $"Replace the infrastructure reference to '{symbol}' with a domain abstraction or revise the explicit {scope}.{kind} restriction '{name}'.",
                evidence.FirstOrDefault(fact => fact.Location is not null)?.Location,
                new ViolationContext([new("project", project), new("symbol", symbol)], [new(scope, kind, [name])]));
        }).ToArray();
        var status = context.Status == AnalysisStatus.Completed && issues.Count > 0 ? AnalysisStatus.Incomplete : context.Status;
        return Task.FromResult(new RuleEvaluationResult(status, violations, issues));
    }
}
