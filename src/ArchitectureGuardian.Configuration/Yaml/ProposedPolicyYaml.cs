using System.Text;
using System.Text.Json;
using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Configuration.Yaml;

/// <summary>Writes deterministic, review-only v1 policy YAML and validates it through the shared reader.</summary>
public static class ProposedPolicyYaml
{
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
        var yaml = text.ToString();
        _ = new YamlPolicyLoader().Parse(yaml, "architecture.guardian.proposed.yaml", cancellationToken);
        return yaml;

        void List(string key, IEnumerable<string> values) => text.Append(key).Append(": [")
            .AppendJoin(", ", values.Order(StringComparer.Ordinal).Select(Quote)).Append("]\n");
    }

    private static string Quote(string value) => JsonSerializer.Serialize(value);
}
