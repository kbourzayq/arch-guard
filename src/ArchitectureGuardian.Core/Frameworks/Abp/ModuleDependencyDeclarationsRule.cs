using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.Abp;

/// <summary>Checks ABP001: a module's [DependsOn] declarations reach every module of the solution projects it references.</summary>
/// <remarks>
/// ABP builds its module graph from [DependsOn], not from project references. A referenced module outside that graph is
/// never configured or initialized, so its services and options are missing at runtime. Transitive declarations count,
/// because ABP loads them too. A reference that another referenced module project also has is checked at that project
/// instead, so a missing declaration is reported once, by the module closest to it.
/// </remarks>
public sealed class ModuleDependencyDeclarationsRule : IArchitectureRule<DependencyBuildResult>
{
    /// <inheritdoc/>
    public RuleDescriptor Descriptor => AbpRuleCatalog.ModuleDependencies;

    /// <summary>Reports each referenced solution project whose modules the referencing project's modules never declare.</summary>
    /// <param name="context">The repository, ABP facts and upstream coverage.</param>
    /// <param name="cancellationToken">Cooperative cancellation during evaluation.</param>
    /// <returns>Proven findings with module evidence, or an incomplete result when facts are unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupts evaluation.</exception>
    public Task<RuleEvaluationResult> EvaluateAsync(DependencyBuildResult context, CancellationToken cancellationToken = default)
        => AbpEvaluation.Run(context, Descriptor, (repository, facts, violations, issues) =>
        {
            var modules = facts.Modules.GroupBy(item => item.Project, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            foreach (var reference in Checked(facts.ProjectReferences, modules.Keys.ToHashSet(StringComparer.Ordinal)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Projects without a module class are not ABP modules, and a referenced project without one has nothing to load.
                if (reference.Project == reference.ReferencedProject || !modules.TryGetValue(reference.Project, out var declaring)
                    || !modules.TryGetValue(reference.ReferencedProject, out var referenced))
                {
                    continue;
                }

                var reachable = declaring.SelectMany(item => item.DependsOn).ToHashSet(StringComparer.Ordinal);
                if (referenced.Any(item => reachable.Contains(item.Module))
                    || AbpEvaluation.Mapping(repository, reference.Project, Descriptor.Id, issues) is not { } source)
                {
                    continue;
                }

                var user = string.Join(", ", declaring.Select(item => item.Module));
                var missing = referenced.Select(item => item.Module).ToArray();
                var names = string.Join("' or '", missing);
                var violation = $"Module '{user}' in project '{reference.Project}' references project '{reference.ReferencedProject}' but does not depend on '{names}'.";
                violations.Add(AbpEvaluation.Violation(Descriptor, source.ModuleId, reference.Project, user,
                    $"{violation} ABP will not configure or initialize that module for this one, so its services and options can be missing at runtime.",
                    $"Add [DependsOn(typeof({missing[0].Split('.')[^1]}))] to '{declaring[0].Module.Split('.')[^1]}', or remove the project reference if it is unused.",
                    declaring.Select(item => new DiagnosticEvidence($"Module '{item.Module}' does not reach '{names}' through [DependsOn].", item.Location)),
                    ViolationClassification.Proven, "dependsOn", missing, string.Join(", ", missing)));
            }
        }, cancellationToken);

    /// <summary>Skips references that another referenced module project also has, since build tools flow references transitively.</summary>
    /// <remarks>
    /// A → C is left to B only when B declares a module, so ABP001 checks B → C itself. A path through a project without a
    /// module would leave the declaration unchecked, so A → C is kept.
    /// </remarks>
    private static IEnumerable<AbpProjectReference> Checked(IReadOnlyList<AbpProjectReference> references, HashSet<string> moduleProjects)
    {
        var targets = references.GroupBy(item => item.Project, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.ReferencedProject).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        return references.Where(reference => !targets[reference.Project].Any(other => other != reference.ReferencedProject
            && moduleProjects.Contains(other) && targets.TryGetValue(other, out var next) && next.Contains(reference.ReferencedProject)));
    }
}
