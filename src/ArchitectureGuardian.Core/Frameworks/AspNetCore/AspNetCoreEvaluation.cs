using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Repositories;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.AspNetCore;

/// <summary>Shares coverage handling and finding construction between the ASP.NET Core rules.</summary>
internal static class AspNetCoreEvaluation
{
    /// <summary>Runs a rule over extracted facts, preserving upstream coverage and reporting unavailable facts.</summary>
    internal static Task<RuleEvaluationResult> Run(DependencyBuildResult context, RuleDescriptor descriptor,
        Action<RepositoryModel, AspNetCoreFacts, List<RuleViolation>, HashSet<AnalysisIssue>> check, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Repository is null || context.Graph is null)
        {
            return Task.FromResult(new RuleEvaluationResult(context.Status, issues: context.Issues));
        }

        var issues = context.Issues.ToHashSet();
        var violations = new List<RuleViolation>();
        if (context.AspNetCore is null)
        {
            // A selected ASP rule without facts must never look like a clean scan.
            issues.Add(new("aspnetcore-facts-unavailable",
                "ASP.NET Core facts were not extracted for this scope, so the rule could not be evaluated.", descriptor.Id));
        }
        else
        {
            check(context.Repository, context.AspNetCore, violations, issues);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var status = context.Status == AnalysisStatus.Completed && issues.Count > 0 ? AnalysisStatus.Incomplete : context.Status;
        return Task.FromResult(new RuleEvaluationResult(status, violations, issues));
    }

    /// <summary>Resolves the owning module, or records an unmapped-project issue and returns null.</summary>
    internal static string? Module(RepositoryModel repository, string project, string ruleId, HashSet<AnalysisIssue> issues)
    {
        if (repository.ResolvedPolicy.Projects.TryGetValue(project, out var owner))
        {
            return owner.ModuleId;
        }

        issues.Add(new("dependency-project-unmapped", $"Project '{project}' has no policy ownership.", ruleId));
        return null;
    }

    /// <summary>Builds a finding whose context path runs from the owning project to the offending symbol.</summary>
    internal static RuleViolation Violation(RuleDescriptor rule, string module, string project, string symbol, string message,
        string remediation, IEnumerable<DiagnosticEvidence> evidence, ViolationClassification classification, string setting,
        IEnumerable<string> values, string targetSymbol)
    {
        var ordered = evidence.OrderBy(item => item.Location?.Path, StringComparer.Ordinal).ThenBy(item => item.Description, StringComparer.Ordinal)
            .ThenBy(item => item.Location?.CanonicalKey, StringComparer.Ordinal).ToArray();
        return new RuleViolation(rule, message, new(module: module, symbol: symbol), ordered, remediation,
            ordered.FirstOrDefault(item => item.Location is not null)?.Location,
            new ViolationContext([new("project", project), new("symbol", targetSymbol)], [new("aspNetCore." + rule.Id, setting, values)]),
            classification);
    }
}
