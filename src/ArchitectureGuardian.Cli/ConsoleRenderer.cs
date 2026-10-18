using System.Globalization;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Repositories;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Cli;

/// <summary>Renders deterministic human-readable facts to stdout and operational failures to stderr.</summary>
/// <param name="output">The stream for diagnostics, inspection and successful summaries.</param>
/// <param name="error">The stream for errors and coverage gaps.</param>
internal sealed class ConsoleRenderer(TextWriter output, TextWriter error)
{
    internal void Explain(RuleDescriptor descriptor)
    {
        output.WriteLine($"{descriptor.Id}: {descriptor.Title}");
        output.WriteLine($"  Documentation: {descriptor.DocumentationUrl}");
        output.WriteLine(descriptor.Id is "AG001" or "AG002" or "AG003"
            ? "  Available in scan; select this ID and its severity in the policy's rules mapping."
            : "  Catalog metadata only; this rule is not yet available in scan.");
    }

    internal void Loaded(int projects, string configuration) => output.WriteLine(
        FormattableString.Invariant($"Workspace loaded: {projects} project(s), configuration {configuration}. No architecture rules evaluated."));

    internal int Coverage(string operation, AnalysisStatus status, IEnumerable<AnalysisIssue> issues)
    {
        if (status == AnalysisStatus.Completed)
        {
            output.WriteLine($"{operation} completed.");
            return 0;
        }

        error.WriteLine($"{operation} {status.ToString().ToLowerInvariant()}. Results are unreliable; resolve the issues and rerun.");
        foreach (var issue in issues.Distinct())
        {
            error.WriteLine($"{Location(issue.Location)}{issue.Code}{(issue.RuleId is null ? "" : $" [{issue.RuleId}]")}: {issue.Message}");
        }

        if (status == AnalysisStatus.Cancelled)
        {
            error.WriteLine("Cancellation requested; analysis did not complete.");
        }

        return 2;
    }

    internal void Gate(bool passed, int count) => output.WriteLine(FormattableString.Invariant(
        $"Analysis completed: {count} diagnostic(s). Gate {(passed ? "passed" : "failed")}."));

    internal void Diagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            output.WriteLine($"{Location(diagnostic.Location)}{diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.RuleId}: {diagnostic.Message}");
            output.WriteLine($"  Target: module={diagnostic.Target.Module ?? "-"}; symbol={diagnostic.Target.Symbol ?? "-"}");
            output.WriteLine($"  ID: {diagnostic.Id}");
            foreach (var evidence in diagnostic.Evidence)
            {
                output.WriteLine($"  Evidence: {Location(evidence.Location)}{evidence.Description}");
            }

            output.WriteLine($"  Remediation: {diagnostic.Remediation}");
            output.WriteLine($"  Documentation: {diagnostic.DocumentationUrl}");
        }
    }

    internal void Inspect(RepositoryModel repository)
    {
        output.WriteLine($"Input: {repository.Id}");
        foreach (var module in repository.Modules)
        {
            output.WriteLine($"Module {module.Id}");
            foreach (var project in module.Projects)
            {
                var mapping = repository.ResolvedPolicy.Projects[project.Id];
                output.WriteLine($"  Project: {project.Id}; layer={mapping.LayerId}; contract={mapping.ContractId ?? "(implementation)"}");
            }

            foreach (var contract in module.Contracts)
            {
                output.WriteLine($"  Contract {contract.Id}: {string.Join(", ", contract.Projects.Select(project => project.Id))}");
            }

            var permissions = repository.Policy.Modules.Single(item => item.Id == module.Id).AllowedDependencies;
            output.WriteLine($"  Allowed dependencies: {Permissions(permissions)}");
        }

        foreach (var layer in repository.Layers)
        {
            output.WriteLine($"Layer {layer.Id}: {string.Join(", ", layer.Projects.Select(project => project.Id))}");
            output.WriteLine($"  Allowed dependencies: {Permissions(repository.Policy.Layers.Single(item => item.Id == layer.Id).AllowedDependencies)}");
        }

        output.WriteLine("Constraints: cross-boundary dependencies require direct permission; same-module/same-layer dependencies are allowed by their respective checks.");
        foreach (var rule in repository.Policy.SeverityPolicy.RuleSeverities.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"Rule {rule.Key}: {rule.Value.ToString().ToLowerInvariant()}");
        }

        output.WriteLine($"Gate: fail on {repository.Policy.Gate.FailOn.ToString().ToLowerInvariant()} or higher. No architecture rules evaluated.");
    }

    private static string Permissions(IReadOnlyList<string> values) => values.Count == 0 ? "(none)" : string.Join(", ", values);

    private static string Location(SourceLocation? location) => location is null ? "" :
        string.Create(CultureInfo.InvariantCulture, $"{location.Path}({location.StartLine},{location.StartColumn}): ");
}
