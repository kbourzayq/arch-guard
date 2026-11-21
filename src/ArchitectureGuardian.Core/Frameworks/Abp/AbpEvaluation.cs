using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Repositories;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.Abp;

/// <summary>Shares coverage handling and finding construction between the ABP rules.</summary>
internal static class AbpEvaluation
{
    /// <summary>Runs a rule over extracted facts, preserving upstream coverage and reporting unavailable facts.</summary>
    internal static Task<RuleEvaluationResult> Run(DependencyBuildResult context, RuleDescriptor descriptor,
        Action<RepositoryModel, AbpFacts, List<RuleViolation>, HashSet<AnalysisIssue>> check, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Repository is null || context.Graph is null)
        {
            return Task.FromResult(new RuleEvaluationResult(context.Status, issues: context.Issues));
        }

        var issues = context.Issues.ToHashSet();
        var violations = new List<RuleViolation>();
        if (context.Abp is null)
        {
            // A selected ABP rule without facts must never look like a clean scan.
            issues.Add(new("abp-facts-unavailable",
                "ABP Framework facts were not extracted for this scope, so the rule could not be evaluated.", descriptor.Id));
        }
        else
        {
            check(context.Repository, context.Abp, violations, issues);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var status = context.Status == AnalysisStatus.Completed && issues.Count > 0 ? AnalysisStatus.Incomplete : context.Status;
        return Task.FromResult(new RuleEvaluationResult(status, violations, issues));
    }

    /// <summary>Resolves the project's ownership, or records an unmapped-project issue and returns null.</summary>
    internal static ProjectPolicyMapping? Mapping(RepositoryModel repository, string project, string ruleId, HashSet<AnalysisIssue> issues)
    {
        if (repository.ResolvedPolicy.Projects.TryGetValue(project, out var mapping))
        {
            return mapping;
        }

        issues.Add(new("dependency-project-unmapped", $"Project '{project}' has no policy ownership.", ruleId));
        return null;
    }

    /// <summary>Builds a finding whose context path runs from the using project to the offending symbol.</summary>
    internal static RuleViolation Violation(RuleDescriptor rule, string module, string project, string user, string message,
        string remediation, IEnumerable<DiagnosticEvidence> evidence, ViolationClassification classification, string setting,
        IEnumerable<string> values, string targetSymbol)
    {
        var ordered = evidence.OrderBy(item => item.Location?.Path, StringComparer.Ordinal).ThenBy(item => item.Description, StringComparer.Ordinal)
            .ThenBy(item => item.Location?.CanonicalKey, StringComparer.Ordinal).ToArray();
        return new RuleViolation(rule, message, new(module: module, symbol: user), ordered, remediation,
            ordered.FirstOrDefault(item => item.Location is not null)?.Location,
            new ViolationContext([new("project", project), new("symbol", targetSymbol)], [new("abp." + rule.Id, setting, values)]),
            classification);
    }
}
