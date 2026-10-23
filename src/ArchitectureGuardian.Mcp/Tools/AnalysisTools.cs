using System.ComponentModel;
using ArchitectureGuardian.Analysis.Changes;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Mcp.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Read-only full solution and Git base/candidate analysis through the shared engine.</summary>
[McpServerToolType]
internal sealed class AnalysisTools(WorkspaceFiles files, IOptions<WorkspaceOptions> options)
{
    /// <summary>Runs the same policy-selected analysis pipeline as CLI scan.</summary>
    [McpServerTool(Name = "analyze_solution", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(AnalysisResponse))]
    [Description(
        "Analyze a full solution/project using the CLI engine. Returns coverage, gate outcome, severity counts and complete evidence-backed diagnostics. Requires operator Workspace:Trusted. Payload limit: 65536 UTF-8 bytes; oversized output is an error, never a clean or truncated result.")]
    public async Task<CallToolResult> AnalyzeSolutionAsync(
        [Description("Solution or C# project path inside Workspace:Root, at most 4096 characters.")]
        string inputPath,
        [Description(
            "YAML policy path inside Workspace:Root, at most 4096 characters and 262144 file bytes; its directory must contain the input and projects.")]
        string policyPath,
        [Description("Build configuration, at most 128 characters.")]
        string configuration = "Debug",
        [Description("Explicit framework for multi-targeted inputs, at most 128 characters.")]
        string? targetFramework = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateBuild(configuration, targetFramework);
        var input = files.Resolve(inputPath);
        var path = files.Resolve(policyPath);
        var policy = await files.LoadPolicyAsync(path, cancellationToken);
        var result = await new SolutionAnalysisService().AnalyzeAsync(
            new WorkspaceLoadRequest(input, options.Value.Trusted, configuration, targetFramework),
            Path.GetDirectoryName(path)!, policy, cancellationToken);
        return Response(result, policy, input, path, configuration, targetFramework, null, "solution",
            cancellationToken);
    }

    /// <summary>Compares complete base and candidate contexts, including each snapshot's policy.</summary>
    [McpServerTool(Name = "analyze_changes", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(AnalysisResponse))]
    [Description(
        "Compare full architecture contexts for an exact Git base and the working tree, including staged, unstaged and non-ignored untracked files. Uses each snapshot's policy; gates new findings. Requires operator trust for both trees and Workspace:Root containing the Git root. Temporary snapshots use cache-only restore; no fetch or package download. Missing refs/coverage cannot pass. Payload limit: 65536 UTF-8 bytes.")]
    public async Task<CallToolResult> AnalyzeChangesAsync(
        [Description("Candidate solution or project path inside Workspace:Root, at most 4096 characters.")]
        string inputPath,
        [Description("YAML policy path inside Workspace:Root, at most 4096 characters and 262144 file bytes.")]
        string policyPath,
        [Description("Exact base commit/ref, at most 256 characters; resolved locally without fetching or selecting a merge base.")]
        string baseReference,
        [Description("Explicit comparison scope. Currently only the exact value solution is defined.")]
        string scope,
        [Description("Candidate build configuration, at most 128 characters.")]
        string configuration = "Debug",
        [Description("Candidate framework, at most 128 characters.")]
        string? targetFramework = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorkspaceFiles.ValidateText(baseReference, nameof(baseReference), 256);
        if (scope != "solution")
        {
            throw new McpException("scope must be the exact value solution.");
        }

        ValidateBuild(configuration, targetFramework);
        var input = files.Resolve(inputPath);
        var path = files.Resolve(policyPath);
        var changes = await new SolutionAnalysisService().AnalyzeChangesAsync(
            new WorkspaceLoadRequest(input, options.Value.Trusted, configuration, targetFramework), path, baseReference,
            new SnapshotPolicyLoader(files), options.Value.Root, cancellationToken);
        return Response(changes.Analysis, changes.CandidatePolicy, input, path, configuration, targetFramework,
            baseReference, scope, cancellationToken, changes);
    }

    private CallToolResult Response(AnalysisResult result, ArchitecturePolicy? policy, string input, string path,
        string configuration, string? targetFramework, string? baseReference, string scope,
        CancellationToken cancellationToken, ChangeAnalysisResult? changes = null)
    {
        // Shared services represent cancellation as coverage; protocol callers retain SDK cancellation semantics.
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = result.Diagnostics.Select(item => new AnalysisDiagnostic(item.Id, item.RuleId,
            item.Severity.ToString().ToLowerInvariant(), item.Message, item.Location, item.Target, item.Evidence,
            item.Remediation, item.DocumentationUrl.AbsoluteUri)).ToArray();
        var counts = new DiagnosticCounts(diagnostics.Length,
                result.Diagnostics.Count(item => item.Severity == DiagnosticSeverity.Info),
                result.Diagnostics.Count(item => item.Severity == DiagnosticSeverity.Warning),
                result.Diagnostics.Count(item => item.Severity == DiagnosticSeverity.Error));
        var comparison = changes?.Comparison is { } classified
            ? new ChangeSummary(changes.BaseCommit!, classified.Existing.Select(item => item.Id).ToArray(),
                classified.New.Select(item => item.Id).ToArray(), classified.Resolved) : null;
        return ToolResults.Create(new AnalysisResponse(1,
            result.Status.ToString().ToLowerInvariant(),
            scope, Relative(input), Relative(path), configuration, targetFramework, baseReference,
            result.IsComplete ? (changes?.GatePassed ?? policy!.Gate.Passes(result)) ? "passed" : "failed" : "not-evaluated",
            policy?.Gate.FailOn.ToString().ToLowerInvariant() ?? "unavailable", counts, diagnostics, result.Issues, comparison), cancellationToken);
    }

    private string Relative(string path) => Path.GetRelativePath(options.Value.Root, path).Replace('\\', '/');

    private static void ValidateBuild(string configuration, string? targetFramework)
    {
        WorkspaceFiles.ValidateText(configuration, nameof(configuration));
        if (targetFramework is not null)
        {
            WorkspaceFiles.ValidateText(targetFramework, nameof(targetFramework));
        }
    }
    /// <summary>Applies MCP policy size and validation limits to both temporary snapshots.</summary>
    private sealed class SnapshotPolicyLoader(WorkspaceFiles files) : IPolicyLoader
    {
        /// <summary>Loads policy with the same bounds as full-solution tools.</summary>
        public Task<ArchitecturePolicy> LoadAsync(string path, CancellationToken cancellationToken = default) => files.LoadPolicyAsync(path, cancellationToken);
    }
}
