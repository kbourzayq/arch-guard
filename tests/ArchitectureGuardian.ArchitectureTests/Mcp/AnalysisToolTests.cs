using System.Text.Json;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Mcp.Hosting;
using ArchitectureGuardian.Mcp.Tools;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Mcp;

/// <summary>Verifies analysis contracts over the SDK and parity with the shared CLI pipeline.</summary>
public sealed class AnalysisToolTests
{
    /// <summary>Unavailable comparison has a stable structured schema, requires a base/scope and never claims zero findings or a passing gate.</summary>
    [Fact]
    public async Task Changes_are_explicitly_unsupported_and_require_base_and_scope()
    {
        using var fixture = new ArchitectureToolTests.InputFiles();
        await ToolRegistrationTests.WithServer(async (client, token) =>
        {
            var tool = Assert.Single(await client.ListToolsAsync(cancellationToken: token), item => item.Name == "analyze_changes");
            var required = tool.JsonSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToArray();
            Assert.Contains("baseReference", required);
            Assert.Contains("scope", required);
            var arguments = Arguments();
            arguments["baseReference"] = "refs/heads/main";
            arguments["scope"] = "solution";
            using var response = await Call(client, "analyze_changes", arguments, token);
            var result = response.RootElement;
            Assert.Equal(1, result.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("unsupported", result.GetProperty("status").GetString());
            Assert.Equal("not-evaluated", result.GetProperty("gateOutcome").GetString());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("counts").ValueKind);
            Assert.Equal("refs/heads/main", result.GetProperty("baseReference").GetString());
            Assert.Equal("solution", result.GetProperty("scope").GetString());
            Assert.Empty(result.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal("change-analysis-unsupported", Assert.Single(result.GetProperty("issues").EnumerateArray()).GetProperty("code").GetString());

            foreach (var (key, value) in new[] { ("baseReference", ""), ("baseReference", new string('x', 257)), ("scope", "changed-lines") })
            {
                var invalid = new Dictionary<string, object?>(arguments) { [key] = value };
                Assert.True((await client.CallToolAsync("analyze_changes", invalid, cancellationToken: token)).IsError);
            }
            foreach (var key in new[] { "baseReference", "scope" })
            {
                var missing = new Dictionary<string, object?>(arguments);
                missing.Remove(key);
                Assert.True((await client.CallToolAsync("analyze_changes", missing, cancellationToken: token)).IsError);
            }
        }, root: fixture.Root); // No build trust or restore is needed for the unsupported interface.
    }

    /// <summary>Untrusted loading retains operational issues; argument errors cannot become a completed scan.</summary>
    [Fact]
    public async Task Solution_without_trust_has_no_gate_decision()
    {
        using var fixture = new ArchitectureToolTests.InputFiles();
        await ToolRegistrationTests.WithServer(async (client, token) =>
        {
            using var response = await Call(client, "analyze_solution", Arguments(), token);
            var result = response.RootElement;
            Assert.Equal("incomplete", result.GetProperty("status").GetString());
            Assert.Equal("not-evaluated", result.GetProperty("gateOutcome").GetString());
            Assert.Contains(result.GetProperty("issues").EnumerateArray(), issue => issue.GetProperty("code").GetString() == "workspace-untrusted");
            foreach (var (key, value) in new[] { ("inputPath", "../outside.slnx"), ("policyPath", "missing.yaml"),
                ("configuration", ""), ("targetFramework", new string('x', 129)) })
            {
                var arguments = Arguments();
                arguments[key] = value;
                Assert.True((await client.CallToolAsync("analyze_solution", arguments, cancellationToken: token)).IsError);
            }
        }, root: fixture.Root);
    }

    /// <summary>Real scans preserve shared finding IDs, evidence, severity gates, partial diagnostics and output bounds.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Solution_matches_shared_cli_analysis_and_preserves_coverage_and_bounds()
    {
        using var fixture = new ArchitectureToolTests.InputFiles();
        var root = fixture.Root;
        var policyPath = Path.Combine(root, "policy.yaml");
        var inputPath = Path.Combine(root, "Input.slnx");
        var appProject = Path.Combine(root, "App", "App.csproj");
        await File.WriteAllTextAsync(appProject, (await File.ReadAllTextAsync(appProject)).Replace("</Project>",
            "<ItemGroup><ProjectReference Include=\"../Library/Library.csproj\" /></ItemGroup></Project>", StringComparison.Ordinal));
        await File.WriteAllTextAsync(Path.Combine(root, "Library", "Target.cs"), "namespace Library; public class Target { }");
        var source = Path.Combine(root, "App", "Use.cs");
        await File.WriteAllTextAsync(source, "public class Use : Library.Target { }");
        var restore = await Repository.Dotnet(TimeSpan.FromSeconds(60), "restore", inputPath, "--source", root, "--disable-parallel", "--disable-build-servers");
        Assert.True(restore.ExitCode == 0, restore.Output + restore.Error);
        await ToolRegistrationTests.WithServer(async (client, token) =>
        {
            using var clean = await Call(client, "analyze_solution", Arguments(), token);
            Assert.Equal("completed", clean.RootElement.GetProperty("status").GetString());
            Assert.Equal("passed", clean.RootElement.GetProperty("gateOutcome").GetString());
            Assert.Equal(0, clean.RootElement.GetProperty("counts").GetProperty("total").GetInt32());

            var denied = ArchitectureToolTests.InputFiles.Policy.Replace("[\"contract:Library/api\"]", "[]", StringComparison.Ordinal);
            await File.WriteAllTextAsync(policyPath, denied, token);
            var policy = await new YamlPolicyLoader().LoadAsync(policyPath, token);
            var shared = await new SolutionAnalysisService().AnalyzeAsync(new WorkspaceLoadRequest(inputPath, true), root, policy, token);
            using var warnings = await Call(client, "analyze_solution", Arguments(), token);
            var result = warnings.RootElement;
            Assert.Equal("completed", result.GetProperty("status").GetString());
            Assert.Equal("passed", result.GetProperty("gateOutcome").GetString());
            Assert.Equal(shared.Diagnostics.Count, result.GetProperty("counts").GetProperty("total").GetInt32());
            Assert.Equal(shared.Diagnostics.Count(item => item.Severity == DiagnosticSeverity.Warning), result.GetProperty("counts").GetProperty("warning").GetInt32());
            var diagnostic = Assert.Single(result.GetProperty("diagnostics").EnumerateArray());
            var finding = Assert.Single(shared.Diagnostics);
            Assert.Equal(finding.Id, diagnostic.GetProperty("id").GetString());
            Assert.Equal("AG001", diagnostic.GetProperty("ruleId").GetString());
            Assert.Equal(finding.Message, diagnostic.GetProperty("message").GetString());
            Assert.Equal(finding.Evidence.Count, diagnostic.GetProperty("evidence").GetArrayLength());
            Assert.Contains(diagnostic.GetProperty("evidence").EnumerateArray(), evidence => evidence.GetProperty("location").ValueKind == JsonValueKind.Object);
            Assert.Equal(finding.Remediation, diagnostic.GetProperty("remediation").GetString());
            Assert.Equal(finding.DocumentationUrl.AbsoluteUri, diagnostic.GetProperty("documentationUrl").GetString());
            Assert.Equal("App", diagnostic.GetProperty("target").GetProperty("module").GetString());

            await File.WriteAllTextAsync(policyPath, denied.Replace("AG001: warning", "AG001: error", StringComparison.Ordinal), token);
            using var failedGate = await Call(client, "analyze_solution", Arguments(), token);
            Assert.Equal("completed", failedGate.RootElement.GetProperty("status").GetString());
            Assert.Equal("failed", failedGate.RootElement.GetProperty("gateOutcome").GetString());
            Assert.Equal(1, failedGate.RootElement.GetProperty("counts").GetProperty("error").GetInt32());
            await File.WriteAllTextAsync(Path.Combine(root, "App", "Broken.cs"), "class Broken : MissingType { }", token);
            using var partial = await Call(client, "analyze_solution", Arguments(), token);
            Assert.Equal("incomplete", partial.RootElement.GetProperty("status").GetString());
            Assert.Equal("not-evaluated", partial.RootElement.GetProperty("gateOutcome").GetString());
            Assert.NotEmpty(partial.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Contains(partial.RootElement.GetProperty("issues").EnumerateArray(), issue => issue.GetProperty("code").GetString() == "compilation-error");

            File.Delete(Path.Combine(root, "App", "Broken.cs"));
            await File.WriteAllTextAsync(source, "public class Use { " + string.Join(" ", Enumerable.Range(0, 600)
                .Select(index => $"public Library.Target Field{index};")) + " }", token);
            var oversized = await client.CallToolAsync("analyze_solution", Arguments(), cancellationToken: token);
            Assert.True(oversized.IsError);
            Assert.Null(oversized.StructuredContent);
            Assert.Contains("65536", Assert.IsType<TextContentBlock>(Assert.Single(oversized.Content)).Text);
        }, root: root, trusted: true);
    }

    /// <summary>Both production entry points observe a pre-cancelled request before any file work.</summary>
    [Fact]
    public async Task Cancelled_calls_do_not_start_analysis()
    {
        using var fixture = new ArchitectureToolTests.InputFiles();
        var options = Options.Create(new WorkspaceOptions { Root = fixture.Root });
        var tools = new AnalysisTools(new WorkspaceFiles(options, new YamlPolicyLoader()), options);
        var token = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.AnalyzeSolutionAsync("missing", "missing", cancellationToken: token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.AnalyzeChangesAsync("missing", "missing", "HEAD", "solution", cancellationToken: token));
    }

    private static Dictionary<string, object?> Arguments() => new() { ["inputPath"] = "Input.slnx", ["policyPath"] = "policy.yaml" };

    private static async Task<JsonDocument> Call(McpClient client, string name, Dictionary<string, object?> arguments, CancellationToken token)
    {
        var result = await client.CallToolAsync(name, arguments, cancellationToken: token);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.False(result.IsError, text);
        Assert.Equal(result.StructuredContent!.Value.GetRawText(), text);
        return JsonDocument.Parse(text);
    }
}
