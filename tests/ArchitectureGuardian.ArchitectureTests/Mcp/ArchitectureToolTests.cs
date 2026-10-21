using System.Text.Json;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Mcp.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Mcp;

/// <summary>Verifies the production tools' observable schemas, shared policy, resolved coverage and bounded inputs/outputs.</summary>
public sealed class ArchitectureToolTests
{
    /// <summary>Policy-only discovery reports canonical selection, effective severity and availability without build trust.</summary>
    [Fact]
    public async Task Rules_match_shared_policy_and_advertise_structured_schemas()
    {
        using var fixture = new InputFiles();
        await ToolRegistrationTests.WithServer(async (client, token) =>
        {
            var tools = await client.ListToolsAsync(cancellationToken: token);
            foreach (var name in new[] { "list_rules", "get_architecture", "analyze_solution", "analyze_changes" })
            {
                var tool = Assert.Single(tools, tool => tool.Name == name);
                Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
                Assert.False(tool.ProtocolTool.Annotations.DestructiveHint);
                Assert.True(tool.ProtocolTool.OutputSchema!.Value.GetProperty("properties").TryGetProperty("schemaVersion", out _));
                Assert.False(tool.JsonSchema.GetProperty("properties").TryGetProperty("cancellationToken", out _));
            }

            var first = await Call(client, "list_rules", new() { ["policyPath"] = "policy.yaml" }, token);
            Assert.Equal(first, await Call(client, "list_rules", new() { ["policyPath"] = "policy.yaml" }, token));
            using var doc = JsonDocument.Parse(first);
            var result = doc.RootElement;
            Assert.Equal("policy", result.GetProperty("scope").GetString());
            Assert.Equal("not-loaded", result.GetProperty("inventoryStatus").GetString());
            var rules = result.GetProperty("rules").EnumerateArray().ToArray();
            Assert.Equal(6, rules.Length);
            var policy = await new YamlPolicyLoader().LoadAsync(Path.Combine(fixture.Root, "policy.yaml"), token);
            Assert.All(rules, rule =>
            {
                var id = rule.GetProperty("id").GetString()!;
                Assert.Equal(policy.SeverityPolicy.RuleSeverities.ContainsKey(id), rule.GetProperty("selected").GetBoolean());
                Assert.Equal(policy.SeverityPolicy.RuleSeverities.TryGetValue(id, out var severity)
                    ? severity.ToString().ToLowerInvariant() : null, rule.GetProperty("effectiveSeverity").GetString());
                Assert.True(rule.GetProperty("available").GetBoolean());
                Assert.Equal("solution", rule.GetProperty("scope").GetString());
            });
            using var filtered = JsonDocument.Parse(await Call(client, "list_rules",
                new() { ["policyPath"] = "policy.yaml", ["ruleId"] = "AG006" }, token));
            Assert.Equal("AG006", Assert.Single(filtered.RootElement.GetProperty("rules").EnumerateArray()).GetProperty("id").GetString());
        }, root: fixture.Root);
    }

    /// <summary>Real workspace normalization resolves contracts/constraints, retains partial findings and validates complete ownership before filtering.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Architecture_preserves_shared_resolution_and_coverage_under_filters()
    {
        using var fixture = new InputFiles();
        var restore = await Repository.Dotnet("restore", Path.Combine(fixture.Root, "Input.slnx"), "--source", fixture.Root,
            "--disable-parallel", "--disable-build-servers");
        Assert.True(restore.ExitCode == 0, restore.Output + restore.Error);
        await ToolRegistrationTests.WithServer(async (client, token) =>
        {
            var arguments = new Dictionary<string, object?> { ["inputPath"] = "Input.slnx", ["policyPath"] = "policy.yaml" };
            using var complete = JsonDocument.Parse(await Call(client, "get_architecture", arguments, token));
            var architecture = complete.RootElement;
            Assert.Equal("completed", architecture.GetProperty("status").GetString());
            Assert.Empty(architecture.GetProperty("issues").EnumerateArray());
            Assert.Equal(2, architecture.GetProperty("projects").GetArrayLength());
            var library = Assert.Single(architecture.GetProperty("modules").EnumerateArray(), item => item.GetProperty("id").GetString() == "Library");
            var contract = Assert.Single(library.GetProperty("contracts").EnumerateArray());
            Assert.Equal("api", contract.GetProperty("id").GetString());
            Assert.Equal("Library/Library.csproj", Assert.Single(contract.GetProperty("projects").EnumerateArray()).GetString());
            var app = Assert.Single(architecture.GetProperty("modules").EnumerateArray(), item => item.GetProperty("id").GetString() == "App");
            Assert.Equal("contract:Library/api", Assert.Single(app.GetProperty("allowedDependencies").EnumerateArray()).GetString());
            Assert.Equal("Forbidden", Assert.Single(app.GetProperty("forbiddenAssemblies").EnumerateArray()).GetString());
            var domain = Assert.Single(architecture.GetProperty("layers").EnumerateArray(), item => item.GetProperty("id").GetString() == "Domain");
            Assert.Equal("System.Net.Abstractions", Assert.Single(domain.GetProperty("domainInfrastructure").GetProperty("allowedNamespaces").EnumerateArray()).GetString());

            arguments["module"] = "Library";
            arguments["layer"] = "Domain";
            using var filtered = JsonDocument.Parse(await Call(client, "get_architecture", arguments, token));
            Assert.Single(filtered.RootElement.GetProperty("projects").EnumerateArray());
            Assert.Single(filtered.RootElement.GetProperty("modules").EnumerateArray());
            Assert.Single(filtered.RootElement.GetProperty("layers").EnumerateArray());

            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "App", "Broken.cs"), "class Broken : MissingType { }", token);
            using var partial = JsonDocument.Parse(await Call(client, "get_architecture", arguments, token));
            Assert.Equal("incomplete", partial.RootElement.GetProperty("status").GetString());
            Assert.Contains(partial.RootElement.GetProperty("issues").EnumerateArray(), item => item.GetProperty("code").GetString() == "compilation-error");
            Assert.Single(partial.RootElement.GetProperty("projects").EnumerateArray());

            // A filter cannot hide an invalid mapping in another module.
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "policy.yaml"), InputFiles.Policy.Replace("App/*.csproj", "Missing/*.csproj", StringComparison.Ordinal), token);
            using var invalid = JsonDocument.Parse(await Call(client, "get_architecture", arguments, token));
            Assert.Equal("incomplete", invalid.RootElement.GetProperty("status").GetString());
            Assert.Empty(invalid.RootElement.GetProperty("modules").EnumerateArray());
            Assert.Contains(invalid.RootElement.GetProperty("issues").EnumerateArray(), item => item.GetProperty("code").GetString() == "repository-mapping-invalid");

            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "policy.yaml"),
                InputFiles.Policy.Replace("[Forbidden]", "[" + new string('A', 70000) + "]", StringComparison.Ordinal), token);
            arguments.Remove("module");
            arguments.Remove("layer");
            var oversized = await client.CallToolAsync("get_architecture", arguments, cancellationToken: token);
            Assert.True(oversized.IsError);
            Assert.Null(oversized.StructuredContent);
            Assert.Contains("65536", Assert.IsType<TextContentBlock>(Assert.Single(oversized.Content)).Text);
            arguments["module"] = "Library";
            using var narrowed = JsonDocument.Parse(await Call(client, "get_architecture", arguments, token));
            Assert.Single(narrowed.RootElement.GetProperty("modules").EnumerateArray());
        }, root: fixture.Root, trusted: true);
    }

    /// <summary>Invalid policy, paths, filters, trust and size cannot yield successful clean context.</summary>
    [Fact]
    public async Task Invalid_inputs_and_untrusted_builds_are_explicit()
    {
        using var fixture = new InputFiles();
        await ToolRegistrationTests.WithServer(async (client, token) =>
        {
            foreach (var arguments in new List<Dictionary<string, object?>>
            {
                new() { ["policyPath"] = "../outside.yaml" },
                new() { ["policyPath"] = "policy.yaml", ["ruleId"] = "ag001" },
                new() { ["policyPath"] = new string('x', 4097) },
                new() { ["policyPath"] = "missing.yaml" },
            })
            {
                Assert.True((await client.CallToolAsync("list_rules", arguments, cancellationToken: token)).IsError);
            }

            var architecture = new Dictionary<string, object?> { ["inputPath"] = "Input.slnx", ["policyPath"] = "policy.yaml" };
            using var untrusted = JsonDocument.Parse(await Call(client, "get_architecture", architecture, token));
            Assert.Equal("incomplete", untrusted.RootElement.GetProperty("status").GetString());
            Assert.Contains(untrusted.RootElement.GetProperty("issues").EnumerateArray(), item => item.GetProperty("code").GetString() == "workspace-untrusted");
            architecture["module"] = "unknown";
            Assert.True((await client.CallToolAsync("get_architecture", architecture, cancellationToken: token)).IsError);
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "policy.yaml"), "version: 99", token);
            var invalid = await client.CallToolAsync("list_rules", new Dictionary<string, object?> { ["policyPath"] = "policy.yaml" }, cancellationToken: token);
            Assert.True(invalid.IsError);
            Assert.Contains("Invalid", Assert.IsType<TextContentBlock>(Assert.Single(invalid.Content)).Text);
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "policy.yaml"), new string('x', 262145), token);
            var oversized = await client.CallToolAsync("list_rules", new Dictionary<string, object?> { ["policyPath"] = "policy.yaml" }, cancellationToken: token);
            Assert.True(oversized.IsError);
            Assert.Contains("262144", Assert.IsType<TextContentBlock>(Assert.Single(oversized.Content)).Text);
        }, root: fixture.Root);
    }

    /// <summary>Response bounds count UTF-8 bytes, never truncate, and respect cancellation before serialization.</summary>
    [Fact]
    public void Output_bounds_and_cancellation_are_enforced()
    {
        Assert.Throws<McpException>(() => ToolResults.Create(new { text = new string('x', 65536) }, CancellationToken.None));
        Assert.Throws<McpException>(() => ToolResults.Create(new { text = new string('é', 32768) }, CancellationToken.None));
        Assert.ThrowsAny<OperationCanceledException>(() => ToolResults.Create(new { text = "small" }, new CancellationToken(true)));
    }

    /// <summary>Explicit tool paths reject links under the configured root, including directory links.</summary>
    [Fact]
    public void Explicit_paths_reject_symbolic_links()
    {
        if (OperatingSystem.IsWindows()) { return; } // Creating links requires OS-specific privileges on Windows.
        using var fixture = new InputFiles();
        var files = new WorkspaceFiles(Microsoft.Extensions.Options.Options.Create(
            new ArchitectureGuardian.Mcp.Hosting.WorkspaceOptions { Root = fixture.Root }), new YamlPolicyLoader());
        File.CreateSymbolicLink(Path.Combine(fixture.Root, "link.yaml"), Path.Combine(fixture.Root, "policy.yaml"));
        Directory.CreateSymbolicLink(Path.Combine(fixture.Root, "linked"), Path.Combine(fixture.Root, "App"));
        Assert.Throws<McpException>(() => files.Resolve("link.yaml"));
        Assert.Throws<McpException>(() => files.Resolve("linked/App.csproj"));
    }

    private static async Task<string> Call(McpClient client, string name, Dictionary<string, object?> arguments, CancellationToken token)
    {
        var result = await client.CallToolAsync(name, arguments, cancellationToken: token);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.False(result.IsError, text);
        Assert.Equal(result.StructuredContent!.Value.GetRawText(), text);
        return text;
    }

    /// <summary>Small package-free input with two ownership scopes and a whole-project contract.</summary>
    internal sealed class InputFiles : IDisposable
    {
        internal const string Policy = """
            version: 1
            modules:
              - id: App
                projects: ["App/*.csproj"]
                allowedDependencies: ["contract:Library/api"]
                forbiddenAssemblies: [Forbidden]
              - id: Library
                projects: ["Library/*.csproj"]
                contracts:
                  - id: api
                    projects: ["Library/*.csproj"]
            layers:
              - id: Application
                projects: ["App/*.csproj"]
                allowedDependencies: [Domain]
              - id: Domain
                projects: ["Library/*.csproj"]
                domainInfrastructure:
                  forbiddenNamespaces: [System.Net]
                  allowedNamespaces: [System.Net.Abstractions]
            rules:
              AG001: warning
              AG004: error
              AG006: info
            """;
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "archguard-mcp-" + Guid.NewGuid().ToString("N"));

        internal InputFiles()
        {
            foreach (var name in new[] { "App", "Library" })
            {
                Directory.CreateDirectory(Path.Combine(Root, name));
                File.WriteAllText(Path.Combine(Root, name, name + ".csproj"),
                    "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            }
            File.Copy(Path.Combine(Repository.Root, "global.json"), Path.Combine(Root, "global.json"));
            File.WriteAllText(Path.Combine(Root, "Input.slnx"), "<Solution><Project Path=\"App/App.csproj\" /><Project Path=\"Library/Library.csproj\" /></Solution>");
            File.WriteAllText(Path.Combine(Root, "policy.yaml"), Policy);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
