using System.ComponentModel;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Rules;
using ArchitectureGuardian.Mcp.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Explains canonical rules or freshly resolved diagnostic instances without inferred evidence or an LLM.</summary>
[McpServerToolType]
internal sealed class ExplanationTools(WorkspaceFiles files, IOptions<WorkspaceOptions> options)
{
    /// <summary>Returns policy-only guidance or reruns the shared analysis to verify an exact finding reference.</summary>
    [McpServerTool(Name = "explain_violation", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ExplanationResponse))]
    [Description("Explain exactly one canonical ruleId or diagnosticId. Rule mode reads policy only; diagnostic mode requires inputPath, reruns the CLI solution engine with operator build trust, and returns actual evidence, directed dependency path and violated policy. Unknown/stale IDs are errors after complete analysis; incomplete coverage remains explicit. No LLM. Payload limit: 65536 UTF-8 bytes.")]
    public async Task<CallToolResult> ExplainViolationAsync(
        [Description("YAML policy path inside Workspace:Root, at most 4096 characters and 262144 file bytes.")] string policyPath,
        [Description("Canonical case-sensitive rule ID AG001–AG006. Mutually exclusive with diagnosticId; omit inputPath for rule guidance.")] string? ruleId = null,
        [Description("Exact 64-character lowercase hexadecimal diagnostic ID from analyze_solution. Requires the same input/policy/build context.")] string? diagnosticId = null,
        [Description("Solution or project path inside Workspace:Root, at most 4096 characters. Required only for diagnostic mode.")] string? inputPath = null,
        [Description("Diagnostic analysis build configuration, at most 128 characters.")] string configuration = "Debug",
        [Description("Explicit diagnostic analysis framework for multi-targeted inputs, at most 128 characters.")] string? targetFramework = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((ruleId is null) == (diagnosticId is null)) { throw new McpException("Supply exactly one of ruleId or diagnosticId."); }
        if (ruleId is not null && RuleCatalog.All.All(rule => rule.Id != ruleId))
        {
            throw new McpException("Unknown ruleId. Use a canonical case-sensitive ID: AG001–AG006.");
        }
        if (diagnosticId is not null && (diagnosticId.Length != 64 || diagnosticId.Any(character => !char.IsAsciiHexDigitLower(character))))
        {
            throw new McpException("diagnosticId must be an exact 64-character lowercase hexadecimal ID from analyze_solution.");
        }
        if (ruleId is not null && inputPath is not null) { throw new McpException("inputPath is only valid with diagnosticId; rule explanations do not load a workspace."); }
        if (diagnosticId is not null && inputPath is null) { throw new McpException("inputPath is required with diagnosticId. Reuse the original analysis input, policy and build context."); }
        WorkspaceFiles.ValidateText(configuration, nameof(configuration));
        if (targetFramework is not null) { WorkspaceFiles.ValidateText(targetFramework, nameof(targetFramework)); }
        var path = files.Resolve(policyPath);
        var policy = await files.LoadPolicyAsync(path, cancellationToken);
        var rules = ArchitectureTools.Rules(policy);
        if (ruleId is not null)
        {
            var guidance = RuleGuidance.For(ruleId, policy);
            return ToolResults.Create(new ExplanationResponse(1, "completed", "rule", Relative(path), null, null, null,
                rules.Single(rule => rule.Id == ruleId), guidance.Explanation, null, [], guidance.Constraints,
                guidance.PublicContracts, guidance.Remediation, []), cancellationToken);
        }

        var input = files.Resolve(inputPath!);
        var result = await new SolutionAnalysisService().AnalyzeAsync(
            new WorkspaceLoadRequest(input, options.Value.Trusted, configuration, targetFramework),
            Path.GetDirectoryName(path)!, policy, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostic = result.Diagnostics.SingleOrDefault(item => item.Id == diagnosticId);
        if (diagnostic is null)
        {
            if (result.IsComplete)
            {
                throw new McpException("Unknown or stale diagnosticId for this input, policy and build context. Rerun analyze_solution and use a current diagnostic ID.");
            }

            return ToolResults.Create(new ExplanationResponse(1, result.Status.ToString().ToLowerInvariant(), "diagnostic",
                Relative(path), Relative(input), configuration, targetFramework, null,
                "The diagnostic reference cannot be verified because analysis did not complete. Resolve the coverage issues and rerun analyze_solution.",
                null, [], [], [], "Resolve the analysis issues before requesting an instance explanation.", result.Issues), cancellationToken);
        }

        var context = diagnostic.Violation.Context ?? throw new McpException("Structured provenance is unavailable for this diagnostic; no dependency path was inferred.");
        var remediation = diagnostic.Remediation;
        if (diagnostic.RuleId is "AG001" or "AG004")
        {
            remediation += context.PublicContracts.Count == 0
                ? " The target module declares no public contracts in this policy."
                : $" Configured public contracts to consider: {string.Join(", ", context.PublicContracts)}. Verify API suitability and independent module/layer permissions; no replacement symbol is inferred.";
        }

        return ToolResults.Create(new ExplanationResponse(1, result.Status.ToString().ToLowerInvariant(), "diagnostic",
            Relative(path), Relative(input), configuration, targetFramework, rules.Single(rule => rule.Id == diagnostic.RuleId),
            diagnostic.Message, new(diagnostic.Id, diagnostic.RuleId, diagnostic.Severity.ToString().ToLowerInvariant(),
                diagnostic.Message, diagnostic.Location, diagnostic.Target, diagnostic.Evidence, diagnostic.Remediation,
                diagnostic.DocumentationUrl.AbsoluteUri), context.DependencyPath, context.PolicyConstraints,
            context.PublicContracts, remediation, result.Issues), cancellationToken);
    }

    private string Relative(string path) => Path.GetRelativePath(options.Value.Root, path).Replace('\\', '/');
}
