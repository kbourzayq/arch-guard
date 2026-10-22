using System.Text.Json;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Mcp.Hosting;
using ArchitectureGuardian.Mcp.Tools;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Mcp;

/// <summary>Exercises deterministic rule and instance explanations through the actual SDK tool boundary.</summary>
public sealed class ExplanationToolTests
{
    /// <summary>All canonical rules have distinct policy-only guidance without fabricated instance evidence or workspace execution.</summary>
    [Fact]
    public async Task Rules_are_policy_only_deterministic_and_use_configured_contracts()
    {
        using var fixture = new ArchitectureToolTests.InputFiles();
        await ToolRegistrationTests.WithServer(async (client, token) =>
        {
            for (var index = 1; index <= 6; index++)
            {
                var id = $"AG00{index}";
                var arguments = RuleArguments(id);
                using var first = await Call(client, arguments, token);
                using var second = await Call(client, arguments, token);
                Assert.Equal(first.RootElement.GetRawText(), second.RootElement.GetRawText());
                var result = first.RootElement;
                Assert.Equal(1, result.GetProperty("schemaVersion").GetInt32());
                Assert.Equal("completed", result.GetProperty("status").GetString());
                Assert.Equal("rule", result.GetProperty("scope").GetString());
                Assert.Equal(id, result.GetProperty("rule").GetProperty("id").GetString());
                Assert.Equal(index is 1 or 4 or 6, result.GetProperty("rule").GetProperty("selected").GetBoolean());
                Assert.Equal(JsonValueKind.Null, result.GetProperty("diagnostic").ValueKind);
                Assert.Equal(JsonValueKind.Null, result.GetProperty("inputPath").ValueKind);
                Assert.Empty(result.GetProperty("dependencyPath").EnumerateArray());
                Assert.NotEmpty(result.GetProperty("policyConstraints").EnumerateArray());
                Assert.Empty(result.GetProperty("issues").EnumerateArray());
                if (index is 1 or 4)
                {
                    Assert.Equal("contract:Library/api", Assert.Single(result.GetProperty("publicContracts").EnumerateArray()).GetString());
                    Assert.Contains("contract:Library/api", result.GetProperty("remediation").GetString());
                }
            }

            foreach (var arguments in new Dictionary<string, object?>[] {
                new() { ["policyPath"] = "policy.yaml" }, RuleArguments("ag001"), RuleArguments("AG007"),
                new() { ["policyPath"] = "policy.yaml", ["diagnosticId"] = "invalid" },
                new() { ["policyPath"] = "policy.yaml", ["diagnosticId"] = new string('a', 64) },
                new() { ["policyPath"] = "policy.yaml", ["diagnosticId"] = new string('A', 64), ["inputPath"] = "Input.slnx" },
                new() { ["policyPath"] = "policy.yaml", ["ruleId"] = "AG001", ["diagnosticId"] = new string('a', 64) },
                new() { ["policyPath"] = "policy.yaml", ["ruleId"] = "AG001", ["inputPath"] = "Input.slnx" },
                new() { ["policyPath"] = "../outside.yaml", ["ruleId"] = "AG001" },
                new() { ["policyPath"] = "policy.yaml", ["ruleId"] = "AG001", ["configuration"] = "" },
                new() { ["policyPath"] = "policy.yaml", ["ruleId"] = "AG001", ["targetFramework"] = new string('x', 129) } })
            {
                var invalid = await client.CallToolAsync("explain_violation", arguments, cancellationToken: token);
                Assert.True(invalid.IsError);
                Assert.Null(invalid.StructuredContent);
            }

            var policy = ArchitectureToolTests.InputFiles.Policy.Replace("[Forbidden]",
                "[" + string.Join(",", Enumerable.Range(0, 900).Select(index => "Assembly" + index + new string('x', 80))) + "]", StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "policy.yaml"), policy, token);
            var oversized = await client.CallToolAsync("explain_violation", RuleArguments("AG005"), cancellationToken: token);
            Assert.True(oversized.IsError);
            Assert.Null(oversized.StructuredContent);
            Assert.Contains("65536", Assert.IsType<TextContentBlock>(Assert.Single(oversized.Content)).Text);
        }, root: fixture.Root);
    }

    /// <summary>Missing coverage cannot establish that a well-formed diagnostic reference is stale.</summary>
    [Fact]
    public async Task Untrusted_analysis_preserves_coverage_without_claiming_a_stale_reference()
    {
        using var fixture = new ArchitectureToolTests.InputFiles();
        await ToolRegistrationTests.WithServer(async (client, token) =>
        {
            using var response = await Call(client, InstanceArguments(new string('a', 64)), token);
            var result = response.RootElement;
            Assert.Equal("incomplete", result.GetProperty("status").GetString());
            Assert.Equal("diagnostic", result.GetProperty("scope").GetString());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("diagnostic").ValueKind);
            Assert.Empty(result.GetProperty("dependencyPath").EnumerateArray());
            Assert.Contains(result.GetProperty("issues").EnumerateArray(), issue => issue.GetProperty("code").GetString() == "workspace-untrusted");
        }, root: fixture.Root);
    }

    /// <summary>Fresh analysis resolves exact IDs, retains reference-only and semantic evidence, and rejects stale references after complete analysis.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Instances_retain_actual_evidence_policy_paths_and_contracts_and_reject_stale_ids()
    {
        using var fixture = new ArchitectureToolTests.InputFiles();
        var project = Path.Combine(fixture.Root, "App", "App.csproj");
        await File.WriteAllTextAsync(project, (await File.ReadAllTextAsync(project, TestContext.Current.CancellationToken)).Replace("</Project>",
            "<ItemGroup><ProjectReference Include=\"../Library/Library.csproj\" /></ItemGroup></Project>", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "Library", "Target.cs"), "namespace Library; public class Target { }", TestContext.Current.CancellationToken);
        var policyPath = Path.Combine(fixture.Root, "policy.yaml");
        var denied = ArchitectureToolTests.InputFiles.Policy.Replace("[\"contract:Library/api\"]", "[]", StringComparison.Ordinal);
        await File.WriteAllTextAsync(policyPath, denied, TestContext.Current.CancellationToken);
        var restore = await Repository.Dotnet(TimeSpan.FromSeconds(60), "restore", Path.Combine(fixture.Root, "Input.slnx"), "--source", fixture.Root,
            "--disable-parallel", "--disable-build-servers");
        Assert.True(restore.ExitCode == 0, restore.Output + restore.Error);
        await ToolRegistrationTests.WithServer(async (client, token) =>
        {
            using var references = await Scan(client, token);
            var reference = Assert.Single(references.RootElement.GetProperty("diagnostics").EnumerateArray());
            var referenceId = reference.GetProperty("id").GetString()!;
            using var explanation = await Call(client, InstanceArguments(referenceId), token);
            var result = explanation.RootElement;
            Assert.Equal("completed", result.GetProperty("status").GetString());
            Assert.Equal("diagnostic", result.GetProperty("scope").GetString());
            Assert.Equal(reference.GetRawText(), result.GetProperty("diagnostic").GetRawText());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("diagnostic").GetProperty("location").ValueKind);
            Assert.All(result.GetProperty("diagnostic").GetProperty("evidence").EnumerateArray(), fact => Assert.Equal(JsonValueKind.Null, fact.GetProperty("location").ValueKind));
            Assert.Equal(["App/App.csproj", "Library/Library.csproj"], result.GetProperty("dependencyPath").EnumerateArray().Select(node => node.GetProperty("identity").GetString()));
            var constraint = Assert.Single(result.GetProperty("policyConstraints").EnumerateArray());
            Assert.Equal("modules.App", constraint.GetProperty("scope").GetString());
            Assert.Equal("allowedDependencies", constraint.GetProperty("setting").GetString());
            Assert.Empty(constraint.GetProperty("values").EnumerateArray());
            Assert.Contains("contract:Library/api", result.GetProperty("remediation").GetString());
            using var repeated = await Call(client, InstanceArguments(referenceId), token);
            Assert.Equal(result.GetRawText(), repeated.RootElement.GetRawText());

            var source = Path.Combine(fixture.Root, "App", "Use.cs");
            await File.WriteAllTextAsync(source, "public class Use : Library.Target { }", token);
            using var semanticScan = await Scan(client, token);
            var semantic = Assert.Single(semanticScan.RootElement.GetProperty("diagnostics").EnumerateArray());
            var semanticId = semantic.GetProperty("id").GetString()!;
            Assert.NotEqual(referenceId, semanticId);
            using var semanticExplanation = await Call(client, InstanceArguments(semanticId), token);
            Assert.Equal(semantic.GetRawText(), semanticExplanation.RootElement.GetProperty("diagnostic").GetRawText());
            Assert.Contains(semanticExplanation.RootElement.GetProperty("diagnostic").GetProperty("evidence").EnumerateArray(), fact => fact.GetProperty("location").ValueKind == JsonValueKind.Object);
            foreach (var id in new[] { referenceId, new string('a', 64) })
            {
                var missing = await client.CallToolAsync("explain_violation", InstanceArguments(id), cancellationToken: token);
                Assert.True(missing.IsError);
                Assert.Contains("Unknown or stale", Assert.IsType<TextContentBlock>(Assert.Single(missing.Content)).Text);
            }

            await File.WriteAllTextAsync(policyPath, denied.Replace("AG001: warning", "AG001: error", StringComparison.Ordinal), token);
            using var severity = await Call(client, InstanceArguments(semanticId), token);
            Assert.Equal("error", severity.RootElement.GetProperty("rule").GetProperty("effectiveSeverity").GetString());

            var broken = Path.Combine(fixture.Root, "App", "Broken.cs");
            await File.WriteAllTextAsync(broken, "class Broken : MissingType { }", token);
            using var partial = await Call(client, InstanceArguments(semanticId), token);
            Assert.Equal("incomplete", partial.RootElement.GetProperty("status").GetString());
            Assert.Equal(semanticId, partial.RootElement.GetProperty("diagnostic").GetProperty("id").GetString());
            Assert.NotEmpty(partial.RootElement.GetProperty("issues").EnumerateArray());
            using var unresolved = await Call(client, InstanceArguments(new string('a', 64)), token);
            Assert.Equal("incomplete", unresolved.RootElement.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, unresolved.RootElement.GetProperty("diagnostic").ValueKind);
            File.Delete(broken);

            await File.WriteAllTextAsync(policyPath, ArchitectureToolTests.InputFiles.Policy, token);
            var stale = await client.CallToolAsync("explain_violation", InstanceArguments(semanticId), cancellationToken: token);
            Assert.True(stale.IsError);
            Assert.Contains("Unknown or stale", Assert.IsType<TextContentBlock>(Assert.Single(stale.Content)).Text);
            await File.WriteAllTextAsync(policyPath, denied, token);
            await File.WriteAllTextAsync(source, "public class Use { " + string.Join(" ", Enumerable.Range(0, 600).Select(index => $"public Library.Target Field{index};")) + " }", token);
            // Obtain the ID from the uncapped shared service; the MCP explanation must reject its oversized evidence.
            var policy = await new YamlPolicyLoader().LoadAsync(policyPath, token);
            var shared = await new ArchitectureGuardian.Analysis.Orchestration.SolutionAnalysisService().AnalyzeAsync(
                new(Path.Combine(fixture.Root, "Input.slnx"), true), fixture.Root, policy, token);
            var oversized = await client.CallToolAsync("explain_violation", InstanceArguments(Assert.Single(shared.Diagnostics).Id), cancellationToken: token);
            Assert.True(oversized.IsError);
            Assert.Null(oversized.StructuredContent);
            Assert.Contains("65536", Assert.IsType<TextContentBlock>(Assert.Single(oversized.Content)).Text);
        }, root: fixture.Root, trusted: true);
    }

    /// <summary>Cancellation is honored before file or workspace access in either mode.</summary>
    [Fact]
    public async Task Precancelled_calls_do_not_read_files()
    {
        using var fixture = new ArchitectureToolTests.InputFiles();
        var options = Options.Create(new WorkspaceOptions { Root = fixture.Root });
        var tools = new ExplanationTools(new WorkspaceFiles(options, new YamlPolicyLoader()), options);
        var token = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.ExplainViolationAsync("missing", ruleId: "AG001", cancellationToken: token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.ExplainViolationAsync("missing", diagnosticId: new string('a', 64), inputPath: "missing", cancellationToken: token));
    }

    private static Dictionary<string, object?> RuleArguments(string id) => new() { ["policyPath"] = "policy.yaml", ["ruleId"] = id };
    private static Dictionary<string, object?> InstanceArguments(string id) => new() { ["policyPath"] = "policy.yaml", ["inputPath"] = "Input.slnx", ["diagnosticId"] = id };

    private static async Task<JsonDocument> Scan(McpClient client, CancellationToken token) => Parse(await client.CallToolAsync("analyze_solution",
        new Dictionary<string, object?> { ["policyPath"] = "policy.yaml", ["inputPath"] = "Input.slnx" }, cancellationToken: token));

    private static async Task<JsonDocument> Call(McpClient client, Dictionary<string, object?> arguments, CancellationToken token) =>
        Parse(await client.CallToolAsync("explain_violation", arguments, cancellationToken: token));

    private static JsonDocument Parse(CallToolResult result)
    {
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.False(result.IsError, text);
        Assert.Equal(text, result.StructuredContent!.Value.GetRawText());
        return JsonDocument.Parse(text);
    }
}
