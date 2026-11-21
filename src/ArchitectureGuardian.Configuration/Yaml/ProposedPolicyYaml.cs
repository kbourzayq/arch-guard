using System.Text;
using System.Text.Json;
using ArchitectureGuardian.Configuration.Validation;
using ArchitectureGuardian.Core.Discovery;
using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Configuration.Yaml;

/// <summary>Writes deterministic, review-only v1 policy YAML and validates it through the shared reader.</summary>
public static class ProposedPolicyYaml
{
    /// <summary>Serializes a proposal, extending its suggested built-in pack when the composed result is equivalent.</summary>
    /// <param name="proposal">A proposal with a candidate policy.</param>
    /// <param name="cancellationToken">Cooperative shared-reader validation cancellation.</param>
    /// <returns>UTF-8-ready text with LF newlines; producing text does not activate policy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="proposal"/> is null.</exception>
    /// <exception cref="ArgumentException">The proposal has no candidate policy.</exception>
    /// <remarks>
    /// The pack form is used only after the shared reader composes it and it resolves every project to the same module,
    /// layer and contract, with the same permissions, restrictions, rules and settings. Otherwise every constraint is written.
    /// </remarks>
    public static string Serialize(PolicyProposal proposal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var policy = proposal.Policy ?? throw new ArgumentException("The proposal has no candidate policy.", nameof(proposal));
        if (proposal.Pack is { } pack && Extending(policy, pack) is var yaml)
        {
            try
            {
                if (Equivalent(new YamlPolicyLoader().Parse(yaml, "architecture.guardian.proposed.yaml", cancellationToken), policy))
                {
                    return yaml;
                }
            }
            catch (PolicyConfigurationException)
            {
                // A pack that cannot express this inventory falls back to the complete standalone form below.
            }
        }

        return Serialize(policy, cancellationToken);
    }

    /// <summary>Serializes every constraint without deriving permissions from observed references.</summary>
    /// <param name="policy">Candidate intentions to review.</param>
    /// <param name="cancellationToken">Cooperative shared-reader validation cancellation.</param>
    /// <returns>UTF-8-ready text with LF newlines; producing text does not activate policy.</returns>
    public static string Serialize(ArchitecturePolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var text = new StringBuilder("# REVIEW REQUIRED: proposed intentions, never an approved or active policy.\n# Inspect ownership and permissions, then validate and scan before manual adoption.\nversion: 1\nmodules:\n");
        foreach (var module in policy.Modules)
        {
            text.Append("  - id: ").Append(Quote(module.Id)).Append('\n');
            List("    projects", module.Projects.Select(pattern => pattern.Value));
            List("    allowedDependencies", module.AllowedDependencies);
            List("    forbiddenAssemblies", module.ForbiddenAssemblies);
            List("    forbiddenNamespaces", module.ForbiddenNamespaces);
            if (module.Contracts.Count > 0)
            {
                text.Append("    contracts:\n");
                foreach (var contract in module.Contracts)
                {
                    text.Append("      - id: ").Append(Quote(contract.Id)).Append('\n');
                    List("        projects", contract.Projects.Select(pattern => pattern.Value));
                }
            }
        }
        text.Append("layers:\n");
        foreach (var layer in policy.Layers)
        {
            text.Append("  - id: ").Append(Quote(layer.Id)).Append('\n');
            List("    projects", layer.Projects.Select(pattern => pattern.Value));
            List("    allowedDependencies", layer.AllowedDependencies);
            List("    forbiddenAssemblies", layer.ForbiddenAssemblies);
            List("    forbiddenNamespaces", layer.ForbiddenNamespaces);
            if (layer.DomainInfrastructure is { } restrictions)
            {
                text.Append("    domainInfrastructure:\n");
                List("      forbiddenAssemblies", restrictions.ForbiddenAssemblies);
                List("      forbiddenNamespaces", restrictions.ForbiddenNamespaces);
                List("      allowedNamespaces", restrictions.AllowedNamespaces);
            }
        }
        text.Append("rules:\n");
        foreach (var (id, severity) in policy.SeverityPolicy.RuleSeverities.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            text.Append("  ").Append(id).Append(": ").Append(severity.ToString().ToLowerInvariant()).Append('\n');
        }
        text.Append("gate:\n  failOn: ").Append(policy.Gate.FailOn.ToString().ToLowerInvariant()).Append('\n');
        if (policy.Abp is { } abp)
        {
            text.Append("abp:\n");
            List("  repositoryLayers", abp.RepositoryLayers);
            List("  tenantFilterBypassLayers", abp.TenantFilterBypassLayers);
        }

        var yaml = text.ToString();
        _ = new YamlPolicyLoader().Parse(yaml, "architecture.guardian.proposed.yaml", cancellationToken);
        return yaml;

        void List(string key, IEnumerable<string> values) => text.Append(key).Append(": [")
            .AppendJoin(", ", values.Order(StringComparer.Ordinal).Select(Quote)).Append("]\n");
    }

    /// <summary>Writes modules, the layers the pack leaves to the policy, and the module rules, inheriting everything else.</summary>
    private static string Extending(ArchitecturePolicy policy, PolicyPackSuggestion pack)
    {
        var text = new StringBuilder("# REVIEW REQUIRED: proposed intentions, never an approved or active policy.\n# Inspect ownership and permissions, then validate and scan before manual adoption.\n")
            .Append("# Extends the built-in ").Append(pack.Id).Append('@').Append(pack.Version)
            .Append(" pack, which supplies layer selectors, permissions, technology restrictions and its rule selections.\n")
            .Append("version: 1\nextends:\n  - pack: ").Append(pack.Id).Append("\n    version: ").Append(pack.Version).Append('\n');
        if (pack.Exclusions.Count > 0)
        {
            text.Append("    # No project supplies these pack declarations.\n");
            List("    exclude", pack.Exclusions);
        }

        text.Append("modules:\n");
        foreach (var module in policy.Modules)
        {
            text.Append("  - id: ").Append(Quote(module.Id)).Append('\n');
            List("    projects", module.Projects.Select(pattern => pattern.Value));
            List("    allowedDependencies", module.AllowedDependencies);
            if (module.Contracts.Count > 0)
            {
                text.Append("    contracts:\n");
                foreach (var contract in module.Contracts)
                {
                    text.Append("      - id: ").Append(Quote(contract.Id)).Append('\n');
                    List("        projects", contract.Projects.Select(pattern => pattern.Value));
                }
            }
        }

        // Only layers without pack selectors, such as hosts and tests, need projects from the policy.
        var owned = policy.Layers.Where(layer => layer.Id is "Host" or "Tests").ToArray();
        if (owned.Length > 0)
        {
            text.Append("layers:\n");
            foreach (var layer in owned)
            {
                text.Append("  - id: ").Append(Quote(layer.Id)).Append('\n');
                List("    projects", layer.Projects.Select(pattern => pattern.Value));
            }
        }

        text.Append("rules:\n");
        foreach (var id in new[] { "AG001", "AG003", "AG004" }.Where(policy.SeverityPolicy.RuleSeverities.ContainsKey))
        {
            text.Append("  ").Append(id).Append(": ").Append(policy.SeverityPolicy.RuleSeverities[id].ToString().ToLowerInvariant()).Append('\n');
        }

        return text.ToString();

        void List(string key, IEnumerable<string> values) => text.Append(key).Append(": [")
            .AppendJoin(", ", values.Order(StringComparer.Ordinal).Select(Quote)).Append("]\n");
    }

    /// <summary>Compares the composed pack form with the candidate over the candidate's own exact inventory.</summary>
    private static bool Equivalent(ArchitecturePolicy composed, ArchitecturePolicy candidate)
    {
        var projects = candidate.Modules.SelectMany(module => module.Projects).Select(pattern => pattern.Value).ToArray();
        ResolvedPolicy resolved;
        try
        {
            resolved = new ResolvedPolicy(composed, projects);
        }
        catch (ArgumentException)
        {
            // A pack selector that matches no project, or overlaps ownership, is not equivalent.
            return false;
        }

        var expected = new ResolvedPolicy(candidate, projects);
        return projects.All(project => resolved.Projects[project] == expected.Projects[project])
            && composed.Layers.Count == candidate.Layers.Count
            && candidate.Layers.All(layer => composed.Layers.SingleOrDefault(item => item.Id == layer.Id) is { } other
                && other.AllowedDependencies.SequenceEqual(layer.AllowedDependencies)
                && other.ForbiddenAssemblies.SequenceEqual(layer.ForbiddenAssemblies)
                && other.ForbiddenNamespaces.SequenceEqual(layer.ForbiddenNamespaces)
                && (other.DomainInfrastructure?.ForbiddenNamespaces ?? []).SequenceEqual(layer.DomainInfrastructure?.ForbiddenNamespaces ?? [])
                && (other.DomainInfrastructure?.ForbiddenAssemblies ?? []).SequenceEqual(layer.DomainInfrastructure?.ForbiddenAssemblies ?? [])
                && (other.DomainInfrastructure?.AllowedNamespaces ?? []).SequenceEqual(layer.DomainInfrastructure?.AllowedNamespaces ?? []))
            && composed.SeverityPolicy.RuleSeverities.Count == candidate.SeverityPolicy.RuleSeverities.Count
            && candidate.SeverityPolicy.RuleSeverities.All(pair => composed.SeverityPolicy.RuleSeverities.TryGetValue(pair.Key, out var severity) && severity == pair.Value)
            && composed.Gate.FailOn == candidate.Gate.FailOn
            && (composed.Abp?.RepositoryLayers ?? []).SequenceEqual(candidate.Abp?.RepositoryLayers ?? [])
            && (composed.Abp?.TenantFilterBypassLayers ?? []).SequenceEqual(candidate.Abp?.TenantFilterBypassLayers ?? []);
    }

    private static string Quote(string value) => JsonSerializer.Serialize(value);
}
