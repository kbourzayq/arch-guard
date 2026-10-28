using System.Collections.ObjectModel;

namespace ArchitectureGuardian.Policy.Documents;

/// <summary>An immutable, validated YAML policy, independent of either analysis engine.</summary>
public sealed class PolicyDocument
{
    internal PolicyDocument(IEnumerable<ModulePolicy> modules, IEnumerable<LayerPolicy> layers,
        IReadOnlyDictionary<string, PolicySeverity> rules, PolicySeverity failOn, string sourcePath,
        IDictionary<string, PolicyPosition> locations)
    {
        Modules = PolicyData.Unique(modules, module => module.Id, "module");
        Layers = PolicyData.Unique(layers, layer => layer.Id, "layer");
        if (Modules.Count == 0 || Layers.Count == 0 || rules.Count == 0)
        {
            throw new ArgumentException("Declare at least one module, layer and selected rule.");
        }
        var targets = new HashSet<string>(Modules.Select(module => "module:" + module.Id), StringComparer.Ordinal);
        targets.UnionWith(Modules.SelectMany(module => module.Contracts.Select(contract => "contract:" + module.Id + "/" + contract.Id)));
        foreach (var module in Modules)
        {
            foreach (var target in module.AllowedDependencies)
            {
                if (!targets.Contains(target))
                {
                    throw new ArgumentException($"modules.{module.Id}.allowedDependencies: Unknown target '{target}'; declare the module or contract and use module:ID or contract:MODULE/ID.");
                }
            }
        }
        foreach (var layer in Layers)
        {
            foreach (var target in layer.AllowedDependencies)
            {
                if (Layers.All(candidate => candidate.Id != target))
                {
                    throw new ArgumentException($"layers.{layer.Id}.allowedDependencies: Unknown layer '{target}'; declare the layer first.");
                }
            }
        }
        Rules = new ReadOnlyDictionary<string, PolicySeverity>(rules.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        FailOn = failOn;
        SourcePath = sourcePath;
        Locations = new ReadOnlyDictionary<string, PolicyPosition>(new Dictionary<string, PolicyPosition>(locations, StringComparer.Ordinal));
    }

    /// <summary>Gets the modules in ordinal identity order.</summary>
    public IReadOnlyList<ModulePolicy> Modules { get; }
    /// <summary>Gets the layers in ordinal identity order.</summary>
    public IReadOnlyList<LayerPolicy> Layers { get; }
    /// <summary>Gets selected rules and their default severities.</summary>
    public IReadOnlyDictionary<string, PolicySeverity> Rules { get; }
    /// <summary>Gets the solution gate threshold.</summary>
    public PolicySeverity FailOn { get; }
    /// <summary>Gets the source document path.</summary>
    public string SourcePath { get; }
    /// <summary>Gets declaration positions keyed by logical field path.</summary>
    public IReadOnlyDictionary<string, PolicyPosition> Locations { get; }
}
