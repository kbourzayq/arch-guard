using System.Collections.ObjectModel;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Policies;

/// <summary>Resolves policy selectors against a complete project inventory without loading files or discovering violations.</summary>
public sealed class ResolvedPolicy
{
    private readonly ArchitecturePolicy _policy;

    /// <summary>Resolves all selectors and rejects overlaps, unmatched projects and selectors matching no project.</summary>
    /// <param name="policy">The validated architecture policy.</param>
    /// <param name="projectPaths">The complete project inventory relative to the policy directory, independent of project/assembly names.</param>
    /// <exception cref="ArgumentException">The inventory is empty or invalid, a selector is unused, or project ownership is missing or ambiguous.</exception>
    public ResolvedPolicy(ArchitecturePolicy policy, IEnumerable<string> projectPaths)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(projectPaths);
        _policy = policy;
        var paths = PolicyData.Strings(projectPaths.Select(ProjectPattern.NormalizeIdentity));
        if (paths.Count == 0)
        {
            throw new ArgumentException("Cannot resolve a policy against an empty project inventory.");
        }

        foreach (var module in policy.Modules)
        {
            RequireMatches(module.Projects, paths, $"modules.{module.Id}.projects");
            foreach (var contract in module.Contracts)
            {
                RequireMatches(contract.Projects, paths, $"modules.{module.Id}.contracts.{contract.Id}.projects");
                if (paths.Any(path => Matches(contract.Projects, path) && !Matches(module.Projects, path)))
                {
                    throw new ArgumentException($"Contract '{module.Id}/{contract.Id}' selects a project outside its module.");
                }
            }
        }

        foreach (var layer in policy.Layers)
        {
            RequireMatches(layer.Projects, paths, $"layers.{layer.Id}.projects");
        }

        var mappings = new Dictionary<string, ProjectPolicyMapping>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var modules = policy.Modules.Where(module => Matches(module.Projects, path)).ToArray();
            var layers = policy.Layers.Where(layer => Matches(layer.Projects, path)).ToArray();
            if (modules.Length != 1 || layers.Length != 1)
            {
                throw new ArgumentException($"Project '{path}' must match exactly one module and one layer; matched modules [{string.Join(", ", modules.Select(module => module.Id))}] and layers [{string.Join(", ", layers.Select(layer => layer.Id))}]. Adjust projects selectors.");
            }

            var contracts = modules[0].Contracts.Where(contract => Matches(contract.Projects, path)).ToArray();
            if (contracts.Length > 1)
            {
                throw new ArgumentException($"Project '{path}' matches multiple public contracts in module '{modules[0].Id}'. Narrow their projects selectors.");
            }

            mappings.Add(path, new(path, modules[0].Id, layers[0].Id, contracts.SingleOrDefault()?.Id));
        }

        Projects = new ReadOnlyDictionary<string, ProjectPolicyMapping>(mappings);
    }

    /// <summary>Gets the immutable, ordinally ordered project mappings.</summary>
    public IReadOnlyDictionary<string, ProjectPolicyMapping> Projects { get; }

    /// <summary>Tests AG001 permissions for an already-discovered direct project dependency.</summary>
    /// <param name="sourceProject">The source project's identity.</param>
    /// <param name="targetProject">The target project's identity.</param>
    /// <returns>True for the same module, a whole-module permission, or the target's exact public-contract permission.</returns>
    /// <exception cref="ArgumentException">A project identity is invalid or absent from the resolved inventory.</exception>
    public bool AllowsModuleDependency(string sourceProject, string targetProject)
    {
        var source = GetProject(sourceProject);
        var target = GetProject(targetProject);
        var module = _policy.Modules.Single(module => module.Id == source.ModuleId);
        return DependencySemantics.AllowsModule(source.ModuleId, target.ModuleId, target.ContractId, module.AllowedDependencies);
    }

    /// <summary>Tests AG002 permissions for an already-discovered direct project dependency.</summary>
    /// <param name="sourceProject">The source project's identity.</param>
    /// <param name="targetProject">The target project's identity.</param>
    /// <returns>True for the same layer or an explicit outgoing layer permission.</returns>
    /// <exception cref="ArgumentException">A project identity is invalid or absent from the resolved inventory.</exception>
    public bool AllowsLayerDependency(string sourceProject, string targetProject)
    {
        var source = GetProject(sourceProject);
        var target = GetProject(targetProject);
        return DependencySemantics.AllowsLayer(source.LayerId, target.LayerId,
            _policy.Layers.Single(layer => layer.Id == source.LayerId).AllowedDependencies);
    }

    private ProjectPolicyMapping GetProject(string path)
    {
        var identity = ProjectPattern.NormalizeIdentity(path);
        return Projects.TryGetValue(identity, out var mapping) ? mapping
            : throw new ArgumentException($"Project '{identity}' is absent from the resolved inventory.");
    }

    private static bool Matches(IReadOnlyList<ProjectPattern> patterns, string path) => patterns.Any(pattern => pattern.IsMatchNormalized(path));

    private static void RequireMatches(IReadOnlyList<ProjectPattern> patterns, IReadOnlyList<string> paths, string location)
    {
        foreach (var pattern in patterns)
        {
            if (!paths.Any(pattern.IsMatchNormalized))
            {
                throw new ArgumentException($"{location}: Selector '{pattern.Value}' matches no project; check its case and path relative to the policy directory.");
            }
        }
    }
}
