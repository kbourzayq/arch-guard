using System.Text.Json;
using System.Text.Json.Serialization;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Discovery;
using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Reporting;

/// <summary>Projects reusable discovery evidence and review intentions into a versioned deterministic JSON report.</summary>
public static class DiscoveryReportSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Serializes observations separately from inferences, proposal validity and architecture compliance.</summary>
    /// <param name="result">Discovery outcome, including incomplete or cancelled evidence.</param>
    /// <returns>One JSON v1 document with LF newlines and no timestamps or checkout root.</returns>
    public static string Serialize(DiscoveryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var policy = result.Proposal.Policy;
        var ready = result.Status == AnalysisStatus.Completed && policy is not null;
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            kind = "architecture-discovery",
            result.Status,
            proposalOutcome = ready ? "readyForReview" : "unresolved",
            reviewRequired = true,
            adoption = "manual",
            requested = new { result.Input, result.Configuration, result.Framework },
            exploredContexts = result.Contexts,
            staticInventory = new { status = "unevaluated", projects = result.StaticProjects, issues = result.StaticIssues },
            observations = result.Observations,
            candidateOwnership = result.Proposal.Ownership,
            inferences = result.Proposal.Inferences,
            unresolvedQuestions = result.Proposal.Questions,
            configurationValidation = ready ? "valid" : "notAvailable",
            proposedPolicy = ready ? PolicyDocument(policy!) : null,
            compliance = result.Compliance is null ? null : new
            {
                result.Compliance.Status,
                gateOutcome = !result.Compliance.IsComplete ? "notEvaluated" : policy!.Gate.Passes(result.Compliance) ? "passed" : "failed",
                result.Compliance.Diagnostics,
                result.Compliance.Issues,
            },
        }, Options) + "\n";
    }

    private static object PolicyDocument(ArchitecturePolicy policy) => new
    {
        version = 1,
        modules = policy.Modules.Select(module => new
        {
            module.Id,
            projects = module.Projects.Select(pattern => pattern.Value),
            contracts = module.Contracts.Select(contract => new { contract.Id, projects = contract.Projects.Select(pattern => pattern.Value) }),
            module.AllowedDependencies,
            module.ForbiddenAssemblies,
            module.ForbiddenNamespaces,
        }),
        layers = policy.Layers.Select(layer =>
        {
            var document = new Dictionary<string, object>
            {
                ["id"] = layer.Id,
                ["projects"] = layer.Projects.Select(pattern => pattern.Value),
                ["allowedDependencies"] = layer.AllowedDependencies,
                ["forbiddenAssemblies"] = layer.ForbiddenAssemblies,
                ["forbiddenNamespaces"] = layer.ForbiddenNamespaces,
            };
            if (layer.DomainInfrastructure is { } restrictions)
            {
                document.Add("domainInfrastructure", new { restrictions.ForbiddenAssemblies, restrictions.ForbiddenNamespaces, restrictions.AllowedNamespaces });
            }
            return document;
        }),
        rules = policy.SeverityPolicy.RuleSeverities.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToString().ToLowerInvariant(), StringComparer.Ordinal),
        gate = new { failOn = policy.Gate.FailOn.ToString().ToLowerInvariant() },
    };
}
