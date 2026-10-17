using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Policies;

/// <summary>Contains a validated v1 architecture policy, independent of YAML, workspace hosts and rule implementations.</summary>
public sealed class ArchitecturePolicy
{
    /// <summary>Snapshots definitions and rejects unknown rule IDs or unresolved dependency references.</summary>
    /// <param name="modules">At least one logical module.</param>
    /// <param name="layers">At least one architectural layer.</param>
    /// <param name="rules">Selected canonical rule IDs and their reporting severities; omitted IDs are not selected.</param>
    /// <param name="gate">The quality gate, or null for the error threshold.</param>
    /// <exception cref="ArgumentException">Required definitions are empty, identities repeat, a rule/severity is unknown or a permission is unresolved.</exception>
    public ArchitecturePolicy(IEnumerable<ModulePolicy> modules, IEnumerable<LayerPolicy> layers,
        IReadOnlyDictionary<string, DiagnosticSeverity> rules, GatePolicy? gate = null)
    {
        Modules = PolicyData.Unique(modules, module => module.Id, "module");
        Layers = PolicyData.Unique(layers, layer => layer.Id, "layer");
        ArgumentNullException.ThrowIfNull(rules);
        if (Modules.Count == 0 || Layers.Count == 0 || rules.Count == 0)
        {
            throw new ArgumentException("Declare at least one module, layer and selected rule.");
        }

        foreach (var ruleId in rules.Keys)
        {
            if (RuleCatalog.All.All(rule => rule.Id != ruleId))
            {
                throw new ArgumentException($"rules.{ruleId}: Unknown rule ID; supported IDs are AG001–AG006 (case-sensitive).");
            }
        }

        SeverityPolicy = new SeverityPolicy(rules);
        Gate = gate ?? new GatePolicy();
        var targets = Modules.Select(module => "module:" + module.Id)
            .Concat(Modules.SelectMany(module => module.Contracts.Select(contract => $"contract:{module.Id}/{contract.Id}")))
            .ToHashSet(StringComparer.Ordinal);
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
    }

    /// <summary>Gets the supported policy schema version.</summary>
    public int Version => 1;

    /// <summary>Gets the immutable module declarations in ordinal identity order.</summary>
    public IReadOnlyList<ModulePolicy> Modules { get; }

    /// <summary>Gets the immutable layer declarations in ordinal identity order.</summary>
    public IReadOnlyList<LayerPolicy> Layers { get; }

    /// <summary>Gets selected rule IDs and severities; the override keys are the explicit selection.</summary>
    public SeverityPolicy SeverityPolicy { get; }

    /// <summary>Gets the independent quality-gate settings.</summary>
    public GatePolicy Gate { get; }

    /// <summary>Selects configured rules and rejects missing implementations before any evaluation occurs.</summary>
    /// <typeparam name="TContext">The neutral context consumed by the available rules.</typeparam>
    /// <param name="availableRules">The implementations supported by the current engine and scope.</param>
    /// <returns>The selected implementations in ordinal rule-ID order.</returns>
    /// <exception cref="ArgumentException">A registration is null, duplicated or missing a descriptor, or a selected implementation is unavailable.</exception>
    public IReadOnlyList<IArchitectureRule<TContext>> SelectRules<TContext>(IEnumerable<IArchitectureRule<TContext>> availableRules)
        where TContext : notnull
    {
        var available = PolicyData.Unique(availableRules, rule => rule.Descriptor?.Id
                ?? throw new ArgumentException("Rule implementations must provide a descriptor."), "rule implementation")
            .ToDictionary(rule => rule.Descriptor.Id, StringComparer.Ordinal);
        var selected = SeverityPolicy.RuleSeverities.Keys.Order(StringComparer.Ordinal).ToArray();
        var missing = selected.Where(id => !available.ContainsKey(id)).ToArray();
        return missing.Length > 0 ?
            throw new ArgumentException($"Selected rules are unavailable for this engine/scope: {string.Join(", ", missing)}.")
            : Array.AsReadOnly([.. selected.Select(id => available[id])]);
    }
}
