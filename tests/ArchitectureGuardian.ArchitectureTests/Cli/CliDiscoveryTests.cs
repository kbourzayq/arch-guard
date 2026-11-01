using System.Text.Json;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using ArchitectureGuardian.Cli;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Policies;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Cli;

/// <summary>Exercises explore, propose, review, validate and manual adoption through the CLI application.</summary>
[Trait("Category", "Integration")]
[Collection(nameof(CliWorkspaceTests))]
public sealed class CliDiscoveryTests
{
    /// <summary>Proves byte stability across checkout paths/enumeration and preserves independently expected forbidden edges.</summary>
    [Fact]
    public async Task Proposals_are_deterministic_review_only_and_independently_scannable()
    {
        var firstRoot = CreateRoot();
        var secondRoot = CreateRoot();
        try
        {
            await PrepareAsync(firstRoot, reverse: false);
            await PrepareAsync(secondRoot, reverse: true);
            var approved = Path.Combine(firstRoot, "architecture.guardian.yaml");
            await File.WriteAllTextAsync(approved, "Existing approved policy must survive.", TestContext.Current.CancellationToken);
            var first = await RunAsync("init", "--discover", firstRoot, "--configuration", "Release", "--trusted");
            Assert.True(first.ExitCode == 0, first.Output + first.Error);
            var second = await RunAsync("init", "--discover", secondRoot, "--configuration", "Release", "--trusted");
            Assert.True(second.ExitCode == 0, second.Output + second.Error);
            Assert.Empty(first.Error);
            Assert.Contains("Review required", first.Output);
            Assert.Contains("Candidate compliance: failed", first.Output);
            var proposalPath = Path.Combine(firstRoot, "architecture.guardian.proposed.yaml");
            var yaml = await File.ReadAllTextAsync(proposalPath, TestContext.Current.CancellationToken);
            Assert.Equal(yaml, await File.ReadAllTextAsync(Path.Combine(secondRoot, "architecture.guardian.proposed.yaml"), TestContext.Current.CancellationToken));
            var reportPath = Path.Combine(firstRoot, "architecture.guardian.discovery.json");
            var report = await File.ReadAllBytesAsync(reportPath, TestContext.Current.CancellationToken);
            Assert.Equal(report, await File.ReadAllBytesAsync(Path.Combine(secondRoot, "architecture.guardian.discovery.json"), TestContext.Current.CancellationToken));
            Assert.DoesNotContain((byte)'\r', report);
            using var json = JsonDocument.Parse(report);
            Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("readyForReview", json.RootElement.GetProperty("proposalOutcome").GetString());
            Assert.Equal("valid", json.RootElement.GetProperty("configurationValidation").GetString());
            Assert.Equal("failed", json.RootElement.GetProperty("compliance").GetProperty("gateOutcome").GetString());
            Assert.Equal(3, json.RootElement.GetProperty("observations").GetProperty("inventory").GetProperty("projects").GetArrayLength());
            Assert.Equal(2, json.RootElement.GetProperty("staticInventory").GetProperty("issues").GetArrayLength());
            Assert.DoesNotContain(firstRoot.Replace('\\', '/'), System.Text.Encoding.UTF8.GetString(report));
            var policy = new YamlPolicyLoader().Parse(yaml, cancellationToken: TestContext.Current.CancellationToken);
            var paths = new[] { "Shop.Domain/Shop.Domain.csproj", "Shop.Application/Shop.Application.csproj", "Shop.Infrastructure/Shop.Infrastructure.csproj" };
            var resolved = new ResolvedPolicy(policy, paths);
            Assert.True(resolved.AllowsLayerDependency(paths[1], paths[0]));
            Assert.False(resolved.AllowsLayerDependency(paths[1], paths[2]));
            var common = new[] { Path.Combine(firstRoot, "Input.slnx"), "--policy", proposalPath, "--configuration", "Release", "--trusted" };
            var inspect = await RunAsync(["inspect", .. common]);
            Assert.True(inspect.ExitCode == 0, inspect.Output + inspect.Error);
            var scan = await RunAsync(["scan", .. common]);
            Assert.True(scan.ExitCode == 1, scan.Output + scan.Error);
            Assert.Contains("AG002:", scan.Output);
            Assert.Equal("Existing approved policy must survive.", await File.ReadAllTextAsync(approved, TestContext.Current.CancellationToken));
            var repeat = await RunAsync("init", "--discover", firstRoot, "--configuration", "Release", "--trusted");
            Assert.Equal(2, repeat.ExitCode);
            Assert.Contains("already exists", repeat.Error);
            Assert.Equal(report, await File.ReadAllBytesAsync(reportPath, TestContext.Current.CancellationToken));
            Assert.Equal(yaml, await File.ReadAllTextAsync(proposalPath, TestContext.Current.CancellationToken));
            // Manual adoption is an explicit developer operation after reviewing the candidate.
            var adopted = Path.Combine(secondRoot, "architecture.guardian.yaml");
            File.Copy(Path.Combine(secondRoot, "architecture.guardian.proposed.yaml"), adopted, overwrite: false);
            Assert.Equal(yaml, await File.ReadAllTextAsync(adopted, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(firstRoot, true); Directory.Delete(secondRoot, true); }
    }

    /// <summary>Guidance leaves ambiguous inputs untouched and untrusted discovery writes only an incomplete evidence report.</summary>
    [Fact]
    public async Task Selection_trust_and_output_protection_precede_evaluation()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "A.slnx"), "<Solution />", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, "B.slnx"), "<Solution />", TestContext.Current.CancellationToken);
            var choices = await RunAsync("init", "--discover", root);
            Assert.Equal(2, choices.ExitCode);
            Assert.Contains("A.slnx", choices.Error);
            Assert.Contains("B.slnx", choices.Error);
            Assert.False(File.Exists(Path.Combine(root, "architecture.guardian.discovery.json")));
            var input = Path.Combine(root, "A.slnx");
            var untrusted = await RunAsync("init", "--discover", input);
            Assert.Equal(2, untrusted.ExitCode);
            Assert.Contains("workspace-untrusted", untrusted.Error);
            Assert.False(File.Exists(Path.Combine(root, "architecture.guardian.proposed.yaml")));
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "architecture.guardian.discovery.json"), TestContext.Current.CancellationToken));
            Assert.Equal("incomplete", report.RootElement.GetProperty("status").GetString());
            Assert.Equal("unresolved", report.RootElement.GetProperty("proposalOutcome").GetString());
            Assert.Equal(0, report.RootElement.GetProperty("exploredContexts").GetArrayLength());
            File.Delete(Path.Combine(root, "architecture.guardian.discovery.json"));
            var proposal = Path.Combine(root, "architecture.guardian.proposed.yaml");
            await File.WriteAllTextAsync(proposal, "preserve", TestContext.Current.CancellationToken);
            var protectedOutput = await RunAsync("init", "--discover", input);
            Assert.Equal(2, protectedOutput.ExitCode);
            Assert.Contains("already exists", protectedOutput.Error);
            Assert.False(File.Exists(Path.Combine(root, "architecture.guardian.discovery.json")));
            Assert.Equal("preserve", await File.ReadAllTextAsync(proposal, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "archguard-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.Copy(Path.Combine(Repository.Root, "global.json"), Path.Combine(root, "global.json"));
        return root;
    }

    private static async Task PrepareAsync(string root, bool reverse)
    {
        var roles = new[] { "Domain", "Application", "Infrastructure" };
        await File.WriteAllTextAsync(Path.Combine(root, "UnusedBroken.csproj"), "<Project>", TestContext.Current.CancellationToken);
        foreach (var role in reverse ? roles.Reverse() : roles)
        {
            var directory = Path.Combine(root, "Shop." + role);
            Directory.CreateDirectory(directory);
            var references = role switch
            {
                "Application" => "<ItemGroup><ProjectReference Include=\"../Shop.Domain/Shop.Domain.csproj\" /><ProjectReference Include=\"../Shop.Infrastructure/Shop.Infrastructure.csproj\" /></ItemGroup>",
                "Domain" => """
                    <ItemGroup Condition="'$(Configuration)' == 'Never'">
                      <ProjectReference Include="../UnusedBroken.csproj" />
                      <ProjectReference Include="../../InactiveOutside.csproj" />
                    </ItemGroup>
                    """,
                _ => "",
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "Shop." + role + ".csproj"), """
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>
                <AssemblyName>Output.ASSEMBLY</AssemblyName></PropertyGroup>REFERENCES</Project>
                """.Replace("ASSEMBLY", role, StringComparison.Ordinal).Replace("REFERENCES", references, StringComparison.Ordinal), TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "Contract.cs"), $"namespace Shop.{role}; public class Contract {{ }} "
                + (role == "Application" ? "public class Consumer : Shop.Infrastructure.Contract { }" : ""), TestContext.Current.CancellationToken);
        }
        await File.WriteAllTextAsync(Path.Combine(root, "Input.slnx"), "<Solution>" + string.Concat((reverse ? roles.Reverse() : roles)
            .Select(role => $"<Project Path=\"Shop.{role}/Shop.{role}.csproj\" />")) + "</Solution>", TestContext.Current.CancellationToken);
        var restore = await Repository.Dotnet(TimeSpan.FromSeconds(60), ["restore", Path.Combine(root, "Input.slnx"), "--source", root, "--disable-parallel", "--disable-build-servers"]);
        Assert.True(restore.ExitCode == 0, restore.Output + restore.Error);
    }

    private static async Task<CommandResult> RunAsync(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await CliApplication.RunAsync(arguments, output, error, TestContext.Current.CancellationToken);
        return new(code, output.ToString(), error.ToString());
    }
}
