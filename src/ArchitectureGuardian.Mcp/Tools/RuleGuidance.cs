using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Renders policy-only rule semantics without claiming source evidence or resolved project ownership.</summary>
internal sealed record RuleGuidance(string Explanation, string Remediation, PolicyConstraint[] Constraints, string[] PublicContracts)
{
    internal static RuleGuidance For(string ruleId, ArchitecturePolicy policy)
    {
        var contracts = policy.Modules.SelectMany(module => module.Contracts.Select(contract => $"contract:{module.Id}/{contract.Id}")).ToArray();
        var guidance = ruleId switch
        {
            "AG001" => new RuleGuidance("Cross-module project dependencies require a direct module:ID or exact contract:MODULE/ID permission; same-module dependencies are allowed.",
                "Remove the dependency or use a configured public contract explicitly permitted by the source module. Layer permissions and AG004 remain independent.",
                policy.Modules.Select(module => new PolicyConstraint("modules." + module.Id, "allowedDependencies", module.AllowedDependencies)).ToArray(), contracts),
            "AG002" => new RuleGuidance("Cross-layer dependencies require the target layer in the source layer's outgoing allowedDependencies; same-layer dependencies are allowed.",
                "Remove or invert the dependency, or explicitly revise the configured layer matrix when intended. Module and contract permissions do not override layers.",
                policy.Layers.Select(layer => new PolicyConstraint("layers." + layer.Id, "allowedDependencies", layer.AllowedDependencies)).ToArray(), []),
            "AG003" => new RuleGuidance("Observed cross-module dependencies must be acyclic. One representative directed cycle is reported per cyclic component; permissions do not exempt cycles.",
                "Break a cycle by removing or inverting a dependency, or move shared abstractions to an independent module.",
                [new("rule:AG003", "acyclicModules", [])], []),
            "AG004" => new RuleGuidance("Cross-module semantic uses must target a declared whole-project public contract. Public C# accessibility and module permissions do not expose implementation projects.",
                "Use a configured public contract or move the interaction inside the target module. Verify API suitability and independent module/layer permissions.",
                policy.Modules.Select(module => new PolicyConstraint("modules." + module.Id, "contracts", module.Contracts.Select(contract => contract.Id))).ToArray(), contracts),
            "AG005" => new RuleGuidance("Semantic uses must respect source module and layer forbiddenAssemblies and forbiddenNamespaces. Assembly names are exact; namespace restrictions include descendants.",
                "Remove the forbidden reference or intentionally revise its explicit module/layer restriction. AG006 exceptions do not override AG005.",
                policy.Modules.SelectMany(module => Restrictions("modules." + module.Id, module.ForbiddenAssemblies, module.ForbiddenNamespaces))
                    .Concat(policy.Layers.SelectMany(layer => Restrictions("layers." + layer.Id, layer.ForbiddenAssemblies, layer.ForbiddenNamespaces))).ToArray(), []),
            "AG006" => new RuleGuidance("Semantic uses in configured layers must respect domainInfrastructure restrictions, except explicitly allowed target namespaces and descendants in that same layer. No layer or technology name is assumed.",
                "Replace the infrastructure reference with an appropriate abstraction or intentionally revise the explicit layer restriction. Exceptions apply only to AG006 in that layer.",
                policy.Layers.Where(layer => layer.DomainInfrastructure is not null).SelectMany(layer =>
                    Restrictions("layers." + layer.Id + ".domainInfrastructure", layer.DomainInfrastructure!.ForbiddenAssemblies, layer.DomainInfrastructure.ForbiddenNamespaces)
                        .Append(new("layers." + layer.Id + ".domainInfrastructure", "allowedNamespaces", layer.DomainInfrastructure.AllowedNamespaces))).ToArray(), []),
            _ => throw new ArgumentOutOfRangeException(nameof(ruleId)),
        };
        if (guidance.PublicContracts.Length > 0)
        {
            guidance = guidance with { Remediation = guidance.Remediation + $" Configured public contracts: {string.Join(", ", guidance.PublicContracts)}. These are declarations, not verified replacement APIs." };
        }
        return guidance;
    }

    private static IEnumerable<PolicyConstraint> Restrictions(string scope, IReadOnlyList<string> assemblies, IReadOnlyList<string> namespaces)
    {
        yield return new(scope, "forbiddenAssemblies", assemblies);
        yield return new(scope, "forbiddenNamespaces", namespaces);
    }
}
