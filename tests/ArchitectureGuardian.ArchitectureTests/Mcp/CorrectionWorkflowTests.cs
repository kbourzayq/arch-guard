using System.Collections.Concurrent;
using System.Text.Json;
using System.Xml.Linq;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;
using Xunit.Sdk;

namespace ArchitectureGuardian.ArchitectureTests.Mcp;

/// <summary>Exercises a client's inspect, detect, explain, edit and verify loop against the production stdio executable.</summary>
[Trait("Category", "Integration")]
public sealed class CorrectionWorkflowTests
{
    /// <summary>Only the client edits fixture inputs; fresh server calls verify a correction using the configured public contract.</summary>
    [Fact]
    public async Task Stdio_client_corrects_a_seeded_dependency_without_server_input_mutation()
    {
        using var fixture = new CorrectionFiles();
        var errors = new ConcurrentQueue<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        try
        {
            await fixture.Restore();
            await fixture.InitializeGit();
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
            startup.CancelAfter(TimeSpan.FromSeconds(20));
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = "dotnet",
                Arguments = [Path.Combine(AppContext.BaseDirectory, "ArchitectureGuardian.Mcp.dll"),
                    "--Workspace:Root", fixture.Root, "--Workspace:Trusted", "true"],
                WorkingDirectory = fixture.Root,
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["MSBUILDDISABLENODEREUSE"] = "1",
                    ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0"
                },
                ShutdownTimeout = TimeSpan.FromSeconds(5),
                StandardErrorLines = line => errors.Enqueue(line)
            });
            await using var client = await McpClient.CreateAsync(transport,
                new McpClientOptions { ProtocolVersion = "2025-11-25" }, cancellationToken: startup.Token);
            startup.CancelAfter(Timeout.InfiniteTimeSpan);
            var tools = await client.ListToolsAsync(cancellationToken: token);
            Assert.Equal(["analyze_changes", "analyze_solution", "explain_violation", "get_architecture", "list_rules"],
                tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
            Assert.All(tools, tool =>
            {
                Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
                Assert.False(tool.ProtocolTool.Annotations.DestructiveHint);
            });

            using var rules = Parse(await fixture.Call(client, "list_rules", new() { ["policyPath"] = "architecture.guardian.yaml" }, token));
            var selected = Assert.Single(rules.RootElement.GetProperty("rules").EnumerateArray(), rule => rule.GetProperty("selected").GetBoolean());
            Assert.Equal("AG001", selected.GetProperty("id").GetString());
            using var architecture = Parse(await fixture.Call(client, "get_architecture", Arguments(), token));
            AssertComplete(architecture.RootElement);
            var modules = architecture.RootElement.GetProperty("modules");
            var consumer = Assert.Single(modules.EnumerateArray(), module => module.GetProperty("id").GetString() == "Consumer");
            Assert.Equal("contract:Library/api", Assert.Single(consumer.GetProperty("allowedDependencies").EnumerateArray()).GetString());
            var library = Assert.Single(modules.EnumerateArray(), module => module.GetProperty("id").GetString() == "Library");
            var contract = Assert.Single(library.GetProperty("contracts").EnumerateArray());
            Assert.Equal("api", contract.GetProperty("id").GetString());
            Assert.Equal("Library.Contracts/Library.Contracts.csproj", Assert.Single(contract.GetProperty("projects").EnumerateArray()).GetString());

            using var before = Parse(await fixture.Call(client, "analyze_solution", Arguments(), token));
            AssertComplete(before.RootElement);
            Assert.Equal("failed", before.RootElement.GetProperty("gateOutcome").GetString());
            var diagnostic = Assert.Single(before.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal("AG001", diagnostic.GetProperty("ruleId").GetString());
            Assert.Contains(diagnostic.GetProperty("evidence").EnumerateArray(), fact => fact.GetProperty("location").ValueKind == JsonValueKind.Object);
            var id = diagnostic.GetProperty("id").GetString()!;
            var explanationArguments = Arguments();
            explanationArguments["diagnosticId"] = id;
            using var explained = Parse(await fixture.Call(client, "explain_violation", explanationArguments, token));
            AssertComplete(explained.RootElement);
            Assert.Equal("diagnostic", explained.RootElement.GetProperty("scope").GetString());
            Assert.Equal(diagnostic.GetRawText(), explained.RootElement.GetProperty("diagnostic").GetRawText());
            Assert.Equal(["Consumer/Consumer.csproj", "Library.Implementation/Library.Implementation.csproj"],
                explained.RootElement.GetProperty("dependencyPath").EnumerateArray().Select(node => node.GetProperty("identity").GetString()));
            var constraint = Assert.Single(explained.RootElement.GetProperty("policyConstraints").EnumerateArray());
            Assert.Equal("modules.Consumer", constraint.GetProperty("scope").GetString());
            Assert.Equal("allowedDependencies", constraint.GetProperty("setting").GetString());
            Assert.Equal("contract:Library/api", Assert.Single(constraint.GetProperty("values").EnumerateArray()).GetString());
            Assert.Equal("contract:Library/api", Assert.Single(explained.RootElement.GetProperty("publicContracts").EnumerateArray()).GetString());
            Assert.Contains("contract:Library/api", explained.RootElement.GetProperty("remediation").GetString());

            // The agent's ordinary file tools perform the correction; MCP never receives a write request.
            await fixture.Correct(token);
            await fixture.Restore();
            using var after = Parse(await fixture.Call(client, "analyze_solution", Arguments(), token));
            AssertComplete(after.RootElement);
            Assert.Equal("passed", after.RootElement.GetProperty("gateOutcome").GetString());
            Assert.Equal(0, after.RootElement.GetProperty("counts").GetProperty("total").GetInt32());
            Assert.Empty(after.RootElement.GetProperty("diagnostics").EnumerateArray());
            var stale = await fixture.Call(client, "explain_violation", explanationArguments, token);
            Assert.True(stale.IsError);
            Assert.Contains("Unknown or stale", Assert.IsType<TextContentBlock>(Assert.Single(stale.Content)).Text);

            var changesArguments = Arguments();
            changesArguments["baseReference"] = "HEAD";
            changesArguments["scope"] = "solution";
            using var changes = Parse(await fixture.Call(client, "analyze_changes", changesArguments, token));
            AssertComplete(changes.RootElement);
            Assert.Equal("passed", changes.RootElement.GetProperty("gateOutcome").GetString());
            Assert.Equal(0, changes.RootElement.GetProperty("counts").GetProperty("total").GetInt32());
            Assert.Empty(changes.RootElement.GetProperty("comparison").GetProperty("new").EnumerateArray());
            Assert.Single(changes.RootElement.GetProperty("comparison").GetProperty("resolved").EnumerateArray());
            var cli = await Repository.Dotnet(TimeSpan.FromSeconds(90), Path.Combine(AppContext.BaseDirectory, "ArchitectureGuardian.Cli.dll"),
                "scan", Path.Combine(fixture.Root, "Correction.slnx"), "--policy", Path.Combine(fixture.Root, "architecture.guardian.yaml"),
                "--configuration", "Release", "--trusted", "--base", "HEAD");
            Assert.True(cli.ExitCode == 0, cli.Output + cli.Error);
            Assert.Contains("0 existing, 0 new, 1 resolved", cli.Output);
        }
        catch (Exception exception)
        {
            throw new XunitException($"{exception}\nServer stderr:\n{string.Join(Environment.NewLine, errors)}");
        }
    }

    private static Dictionary<string, object?> Arguments() => new()
    {
        ["inputPath"] = "Correction.slnx",
        ["policyPath"] = "architecture.guardian.yaml",
        ["configuration"] = "Release"
    };

    private static void AssertComplete(JsonElement result)
    {
        Assert.True(result.GetProperty("status").GetString() == "completed", result.GetRawText());
        Assert.Empty(result.GetProperty("issues").EnumerateArray());
    }

    private static JsonDocument Parse(CallToolResult result)
    {
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.False(result.IsError, text);
        Assert.Equal(text, result.StructuredContent!.Value.GetRawText());
        return JsonDocument.Parse(text);
    }

    /// <summary>Owns an isolated fixture copy and checks that every tool call preserves all non-build inputs byte for byte.</summary>
    private sealed class CorrectionFiles : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "archguard-correction-" + Guid.NewGuid().ToString("N"));

        internal CorrectionFiles()
        {
            var source = Path.Combine(Repository.Root, "tests", "Fixtures", "AgentCorrection");
            foreach (var file in Inputs(source))
            {
                var destination = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            File.Copy(Path.Combine(Repository.Root, "global.json"), Path.Combine(Root, "global.json"));
        }

        internal async Task Restore()
        {
            var result = await Repository.Dotnet(TimeSpan.FromSeconds(60), "restore", Path.Combine(Root, "Correction.slnx"),
                "-p:Configuration=Release", "--disable-parallel", "--disable-build-servers");
            Assert.True(result.ExitCode == 0, result.Output + result.Error);
        }

        internal async Task InitializeGit()
        {
            File.WriteAllText(Path.Combine(Root, ".gitignore"), "**/bin/\n**/obj/\n");
            await ChangeInputFiles.GitAt(Root, "init", "-b", "main");
            await ChangeInputFiles.GitAt(Root, "add", ".");
            await ChangeInputFiles.GitAt(Root, "-c", "user.name=Architecture Tests", "-c", "user.email=tests@example.invalid", "commit", "-m", "seeded violation");
        }

        internal async Task Correct(CancellationToken token)
        {
            await File.WriteAllTextAsync(Path.Combine(Root, "Consumer", "Checkout.cs"), """
                namespace Consumer;

                public sealed class Checkout
                {
                    public void Pay(Library.Contracts.IInvoice invoice) => invoice.Pay();
                }
                """, token);
            var path = Path.Combine(Root, "Consumer", "Consumer.csproj");
            var project = XDocument.Load(path);
            project.Descendants("ProjectReference").Single(reference =>
                reference.Attribute("Include")!.Value == "../Library.Implementation/Library.Implementation.csproj").Remove();
            project.Save(path);
        }

        internal async Task<CallToolResult> Call(McpClient client, string tool, Dictionary<string, object?> arguments, CancellationToken token)
        {
            var before = Snapshot();
            var result = await client.CallToolAsync(tool, arguments, cancellationToken: token);
            var after = Snapshot();
            Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
            foreach (var (path, bytes) in before) { Assert.Equal(bytes, after[path]); }
            return result;
        }

        private Dictionary<string, byte[]> Snapshot() => Inputs(Root).ToDictionary(path => Path.GetRelativePath(Root, path), File.ReadAllBytes, StringComparer.Ordinal);

        private static IEnumerable<string> Inputs(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

        /// <summary>Removes fixture inputs and build outputs after the protocol client has shut down.</summary>
        public void Dispose()
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) { File.SetAttributes(file, FileAttributes.Normal); }
            Directory.Delete(Root, recursive: true);
        }
    }
}
