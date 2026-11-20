using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Repositories;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.AspNetCore;

/// <summary>Shares the persistence check between the controller and minimal API rules.</summary>
internal static class PersistenceFindings
{
    internal static void Add(RuleDescriptor rule, AspNetCoreEndpointKind[] kinds, string subject, RepositoryModel repository,
        AspNetCoreFacts facts, List<RuleViolation> violations, HashSet<AnalysisIssue> issues, CancellationToken cancellationToken)
    {
        foreach (var endpoint in facts.Endpoints.Where(item => kinds.Contains(item.Kind)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AspNetCoreEvaluation.Module(repository, endpoint.Project, rule.Id, issues) is not { } module)
            {
                continue;
            }

            foreach (var group in endpoint.Dependencies.Where(AspNetCoreSemantics.IsPersistence)
                .GroupBy(item => item.Symbol, StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var evidence = group.Select(item => new DiagnosticEvidence(
                    $"The {subject} '{endpoint.Symbol}' uses persistence type '{group.Key}'.", item.Location ?? endpoint.Location));
                violations.Add(AspNetCoreEvaluation.Violation(rule, module, endpoint.Project, endpoint.Symbol,
                    $"The {subject} '{endpoint.Symbol}' in project '{endpoint.Project}' uses persistence type '{group.Key}' directly.",
                    $"Move the data access behind an application service or repository abstraction and depend on that from the {subject}.",
                    evidence, ViolationClassification.Proven, "persistenceNamespaces", AspNetCoreSemantics.Namespaces, group.Key));
            }
        }
    }
}
