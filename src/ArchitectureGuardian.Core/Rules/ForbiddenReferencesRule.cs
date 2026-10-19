using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Rules;

/// <summary>Checks AG005 restrictions against bound symbol identities, including framework and external references.</summary>
public sealed class ForbiddenReferencesRule : IArchitectureRule<DependencyBuildResult>
{
    /// <inheritdoc/>
    public RuleDescriptor Descriptor => RuleCatalog.ForbiddenReferences;

    /// <summary>Groups semantic uses by project, symbol and violated module or layer restriction.</summary>
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
        var modules = repository.Policy.Modules.ToDictionary(module => module.Id, StringComparer.Ordinal);
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

            var module = modules[source.ModuleId];
            var layer = layers[source.LayerId];
            Check("modules." + module.Id, module.ForbiddenAssemblies, module.ForbiddenNamespaces);
            Check("layers." + layer.Id, layer.ForbiddenAssemblies, layer.ForbiddenNamespaces);

            void Check(string scope, IReadOnlyList<string> assemblies, IReadOnlyList<string> namespaces)
            {
                if (assemblies.Count + namespaces.Count == 0)
                {
                    return;
                }

                if (evidence.TargetAssembly is null || evidence.TargetNamespace is null)
                {
                    issues.Add(new("dependency-reference-identity-unavailable",
                        $"Bound assembly/namespace identity for '{symbol}' in '{evidence.SourceProject}' is unavailable.", Descriptor.Id,
                        evidence.Fact.Location));
                    return;
                }

                foreach (var name in assemblies)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (evidence.TargetAssembly == name) { Add("forbiddenAssemblies", name); }
                }

                foreach (var name in namespaces)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ForbiddenReferenceSemantics.MatchesNamespace(evidence.TargetNamespace, name)) { Add("forbiddenNamespaces", name); }
                }

                void Add(string kind, string name)
                {
                    var key = (evidence.SourceProject, symbol, scope, kind, name);
                    if (!uses.TryGetValue(key, out var facts)) { uses.Add(key, facts = []); }
                    facts.Add(evidence.Fact);
                }
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
                $"Remove the reference to '{symbol}' or revise the explicit {scope}.{kind} restriction '{name}'.",
                evidence.FirstOrDefault(fact => fact.Location is not null)?.Location);
        }).ToArray();
        var status = context.Status == AnalysisStatus.Completed && issues.Count > 0 ? AnalysisStatus.Incomplete : context.Status;
        return Task.FromResult(new RuleEvaluationResult(status, violations, issues));
    }
}
