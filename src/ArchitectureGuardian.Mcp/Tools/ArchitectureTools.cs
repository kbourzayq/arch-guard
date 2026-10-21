using System.ComponentModel;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Repositories;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Repositories;
using ArchitectureGuardian.Core.Rules;
using ArchitectureGuardian.Mcp.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Exposes shared policy metadata and trusted repository ownership through read-only MCP tools.</summary>
[McpServerToolType]
internal sealed class ArchitectureTools(WorkspaceFiles files, IOptions<WorkspaceOptions> options)
{
    /// <summary>Loads policy metadata without executing repository build logic or claiming resolved project coverage.</summary>
    [McpServerTool(Name = "list_rules", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(RulesResponse))]
    [Description("List canonical rules, configured severities, selection and solution-engine availability from the CLI YAML policy. Policy-only context; no build execution. Payload limit: 65536 UTF-8 bytes.")]
    public async Task<CallToolResult> ListRulesAsync(
        [Description("YAML policy path relative to Workspace:Root (or absolute inside it), at most 4096 characters and 262144 file bytes.")] string policyPath,
        [Description("Optional exact case-sensitive canonical rule ID (AG001–AG006).")] string? ruleId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ruleId is not null && RuleCatalog.All.All(rule => rule.Id != ruleId))
        {
            throw new McpException("Unknown ruleId. Use a canonical case-sensitive ID: AG001–AG006.");
        }

        var path = files.Resolve(policyPath);
        var policy = await files.LoadPolicyAsync(path, cancellationToken);
        return ToolResults.Create(new RulesResponse(1, "completed", "policy", Relative(path), "not-loaded",
            Rules(policy).Where(rule => ruleId is null || rule.Id == ruleId).ToArray()), cancellationToken);
    }

    /// <summary>Normalizes a trusted input through the CLI's workspace and repository services, preserving partial coverage.</summary>
    [McpServerTool(Name = "get_architecture", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ArchitectureResponse))]
    [Description("Resolve modules, public contracts, layers, project ownership and policy constraints using the CLI policy and workspace services. Requires operator Workspace:Trusted for builds. Inspect status/issues before using partial context. Payload limit: 65536 UTF-8 bytes; filter by module/layer as needed.")]
    public async Task<CallToolResult> GetArchitectureAsync(
        [Description("Solution or C# project path relative to Workspace:Root (or absolute inside it), at most 4096 characters.")] string inputPath,
        [Description("YAML policy path relative to Workspace:Root (or absolute inside it), at most 4096 characters and 262144 file bytes. Its directory must contain the input and projects.")] string policyPath,
        [Description("Optional exact case-sensitive module ID, at most 128 characters.")] string? module = null,
        [Description("Optional exact case-sensitive layer ID, at most 128 characters. Combined filters intersect projects.")] string? layer = null,
        [Description("Build configuration, at most 128 characters.")] string configuration = "Debug",
        [Description("Explicit target framework for multi-targeted inputs, at most 128 characters.")] string? targetFramework = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorkspaceFiles.ValidateText(configuration, nameof(configuration));
        if (targetFramework is not null) { WorkspaceFiles.ValidateText(targetFramework, nameof(targetFramework)); }
        var input = files.Resolve(inputPath);
        var path = files.Resolve(policyPath);
        var policy = await files.LoadPolicyAsync(path, cancellationToken);
        ValidateFilter(module, policy.Modules.Select(item => item.Id), "module");
        ValidateFilter(layer, policy.Layers.Select(item => item.Id), "layer");
        using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(
            new(input, options.Value.Trusted, configuration, targetFramework), cancellationToken);
        var result = await new RepositoryModelBuilder().BuildAsync(workspace, Path.GetDirectoryName(path)!, policy, cancellationToken);
        // Shared services represent cancellation as coverage; preserve protocol cancellation when requested.
        cancellationToken.ThrowIfCancellationRequested();
        var projects = result.Model?.ResolvedPolicy.Projects.Values
            .Where(item => (module is null || item.ModuleId == module) && (layer is null || item.LayerId == layer))
            .Select(item => new ProjectContext(item.ProjectPath, item.ModuleId, item.LayerId, item.ContractId)).ToArray() ?? [];
        var members = projects.Select(project => project.Path).ToHashSet(StringComparer.Ordinal);
        var modules = result.Model is null ? [] : policy.Modules
            .Where(item => module is null || item.Id == module)
            .Where(item => layer is null || projects.Any(project => project.Module == item.Id))
            .Select(item => Module(item, result.Model, members)).ToArray();
        var layers = result.Model is null ? [] : policy.Layers
            .Where(item => layer is null || item.Id == layer)
            .Where(item => module is null || projects.Any(project => project.Layer == item.Id))
            .Select(item => Layer(item, result.Model, members)).ToArray();
        return ToolResults.Create(new ArchitectureResponse(1, result.Status.ToString().ToLowerInvariant(), "repository",
            Relative(path), Relative(input), configuration, targetFramework, module, layer,
            result.Issues.Select(issue => new ContextIssue(issue.Code, issue.Message)).ToArray(), modules, layers, projects,
            ["AG001: same-module access is allowed; cross-module permissions are direct module:ID or contract:MODULE/ID references.",
                "AG002: same-layer access is allowed; cross-layer permissions are direct layer IDs.",
                "AG004: cross-module semantic access requires a public-contract project, independently of AG001/AG002 permissions.",
                "AG005/AG006: assembly names are exact; namespace restrictions include descendants. AG006 exceptions apply only within that layer and rule."],
            Rules(policy), policy.Gate.FailOn.ToString().ToLowerInvariant()), cancellationToken);
    }

    private string Relative(string path) => Path.GetRelativePath(options.Value.Root, path).Replace('\\', '/');

    private static void ValidateFilter(string? value, IEnumerable<string> ids, string name)
    {
        if (value is null) { return; }
        WorkspaceFiles.ValidateText(value, name);
        if (!ids.Contains(value, StringComparer.Ordinal)) { throw new McpException($"Unknown {name} filter; use a declared case-sensitive ID."); }
    }

    private static ModuleContext Module(ModulePolicy policy, RepositoryModel model, HashSet<string> members)
    {
        var module = model.Modules.Single(item => item.Id == policy.Id);
        return new(policy.Id, module.Projects.Select(item => item.Id).Where(members.Contains).ToArray(),
            module.Contracts.Select(contract => new ContractContext(contract.Name,
                contract.Projects.Select(item => item.Id).Where(members.Contains).ToArray())).ToArray(),
            policy.AllowedDependencies, policy.ForbiddenAssemblies, policy.ForbiddenNamespaces);
    }

    private static LayerContext Layer(LayerPolicy policy, RepositoryModel model, HashSet<string> members) => new(policy.Id,
        model.Layers.Single(item => item.Id == policy.Id).Projects.Select(item => item.Id).Where(members.Contains).ToArray(),
        policy.AllowedDependencies, policy.ForbiddenAssemblies, policy.ForbiddenNamespaces,
        policy.DomainInfrastructure is { } constraints ? new(constraints.ForbiddenAssemblies, constraints.ForbiddenNamespaces, constraints.AllowedNamespaces) : null);

    private static RuleContext[] Rules(ArchitecturePolicy policy)
    {
        var available = SolutionRules.All.Select(rule => rule.Descriptor.Id).ToHashSet(StringComparer.Ordinal);
        return RuleCatalog.All.Select(rule => new RuleContext(rule.Id, rule.Title, rule.DocumentationUrl.AbsoluteUri,
            policy.SeverityPolicy.RuleSeverities.ContainsKey(rule.Id),
            policy.SeverityPolicy.RuleSeverities.TryGetValue(rule.Id, out var severity) ? severity.ToString().ToLowerInvariant() : null,
            "solution", available.Contains(rule.Id))).ToArray();
    }
}
