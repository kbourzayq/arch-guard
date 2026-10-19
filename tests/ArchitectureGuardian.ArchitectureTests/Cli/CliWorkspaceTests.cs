using System.Reflection;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Cli;

/// <summary>Exercises policy-driven commands against restored projects with a real forbidden dependency.</summary>
[Trait("Category", "Integration")]
public sealed class CliWorkspaceTests
{
    /// <summary>Checks actual findings, severity gates, partial coverage and inspection from the public process interface.</summary>
    [Fact]
    public async Task Scan_and_inspect_preserve_policy_and_coverage_contracts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "archguard-commands-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "App"));
            Directory.CreateDirectory(Path.Combine(directory, "Library"));
            await File.WriteAllTextAsync(Path.Combine(directory, "App", "App.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(directory, "Library", "Library.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(directory, "Library", "Contract.cs"), "namespace Library; public class Contract { }");
            var source = Path.Combine(directory, "App", "Consumer.cs");
            await File.WriteAllTextAsync(source, "public class Consumer : Library.Contract { }");
            var solution = Path.Combine(directory, "Input.slnx");
            await File.WriteAllTextAsync(solution, "<Solution><Project Path=\"App/App.csproj\" /><Project Path=\"Library/Library.csproj\" /></Solution>");
            // Restore through the root project so aliased temporary paths cannot schedule its reference twice.
            var restore = await Repository.Dotnet("restore", Path.Combine(directory, "App", "App.csproj"),
                "--source", directory, "--disable-parallel", "--disable-build-servers");
            Assert.True(restore.ExitCode == 0, restore.Output + restore.Error);
            var policyPath = Path.Combine(directory, "policy.yaml");

            // Warnings remain findings; only the separate configured gate changes acceptance.
            foreach (var (severity, threshold, allowed, expected) in new[]
            {
                ("error", "error", false, 1),
                ("warning", "error", false, 0),
                ("warning", "warning", false, 1),
                ("error", "error", true, 0),
            })
            {
                await File.WriteAllTextAsync(policyPath, Policy(severity, threshold, allowed));
                var result = await RunCli("scan", solution, "--policy", policyPath, "--trusted", "--configuration", "Release", "--framework", "net10.0");
                Assert.True(result.ExitCode == expected, result.Output + result.Error);
                Assert.Empty(result.Error);
                Assert.Contains(expected == 0 ? "Gate passed" : "Gate failed", result.Output);
                if (!allowed)
                {
                    Assert.Contains($"{severity} AG001:", result.Output);
                    Assert.Contains($"{severity} AG002:", result.Output);
                    Assert.Contains("Consumer.cs(1,", result.Output);
                    Assert.Contains("Target: module=App", result.Output);
                    Assert.Contains("Evidence:", result.Output);
                    Assert.Contains("Remediation:", result.Output);
                    Assert.Contains("Documentation:", result.Output);
                }
                else
                {
                    Assert.Contains("0 diagnostic(s)", result.Output);
                }
            }

            var inspect = await RunCli("inspect", solution, "--policy", policyPath, "--trusted");
            Assert.Equal(0, inspect.ExitCode);
            Assert.Empty(inspect.Error);
            Assert.Contains("Module App", inspect.Output);
            Assert.Contains("App/App.csproj; layer=Application; contract=(implementation)", inspect.Output);
            Assert.Contains("Contract Library/api: Library/Library.csproj", inspect.Output);
            Assert.Contains("Allowed dependencies: contract:Library/api", inspect.Output);
            Assert.Contains("Layer Domain:", inspect.Output);
            Assert.Contains("Rule AG003: error", inspect.Output);
            Assert.Contains("No architecture rules evaluated", inspect.Output);

            // Project input includes its referenced project and uses the same policy-relative inventory.
            var projectScan = await RunCli("scan", Path.Combine(directory, "App", "App.csproj"), "--policy", policyPath, "--trusted");
            Assert.True(projectScan.ExitCode == 0, projectScan.Output + projectScan.Error);

            // AG004 treats public types outside declared contracts as implementation, even with module permission.
            var contractPolicy = Policy("error", "error", true).Replace("AG003: error", "AG004: error", StringComparison.Ordinal);
            await File.WriteAllTextAsync(policyPath, contractPolicy);
            var contractScan = await RunCli("scan", solution, "--policy", policyPath, "--trusted");
            Assert.True(contractScan.ExitCode == 0, contractScan.Output + contractScan.Error);
            var implementationPolicy = contractPolicy.Replace("contract:Library/api", "module:Library", StringComparison.Ordinal)
                .Replace("    contracts:\n      - id: api\n        projects: [\"Library/*.csproj\"]\n", "", StringComparison.Ordinal);
            await File.WriteAllTextAsync(policyPath, implementationPolicy);
            var implementationScan = await RunCli("scan", solution, "--policy", policyPath, "--trusted");
            Assert.True(implementationScan.ExitCode == 1, implementationScan.Output + implementationScan.Error);
            Assert.Empty(implementationScan.Error);
            Assert.Contains("error AG004:", implementationScan.Output);
            Assert.Contains("symbol=Library.Contract", implementationScan.Output);
            Assert.Contains("Consumer.cs(1,", implementationScan.Output);
            Assert.DoesNotContain("AG001:", implementationScan.Output);
            Assert.DoesNotContain("AG002:", implementationScan.Output);

            // AG005 is selected through the CLI and bans references even to an allowed public contract.
            foreach (var field in new[] { "forbiddenAssemblies", "forbiddenNamespaces" })
            {
                var restrictedPolicy = Policy("error", "error", true).Replace("AG003: error", "AG005: error", StringComparison.Ordinal)
                    .Replace("  - id: App\n", $"  - id: App\n    {field}: [Library]\n", StringComparison.Ordinal);
                await File.WriteAllTextAsync(policyPath, restrictedPolicy);
                var restricted = await RunCli("scan", solution, "--policy", policyPath, "--trusted");
                Assert.True(restricted.ExitCode == 1, restricted.Output + restricted.Error);
                Assert.Empty(restricted.Error);
                Assert.Contains("error AG005:", restricted.Output);
                Assert.Contains($"modules.App.{field} restriction 'Library'", restricted.Output);
                Assert.Contains("symbol=Library.Contract", restricted.Output);
                Assert.Contains("Consumer.cs(1,", restricted.Output);
                Assert.DoesNotContain("AG001:", restricted.Output);
                var restrictions = await RunCli("inspect", solution, "--policy", policyPath, "--trusted");
                Assert.Equal(0, restrictions.ExitCode);
                Assert.Contains(field == "forbiddenAssemblies" ? "Forbidden assemblies: Library"
                    : "Forbidden namespaces (including descendants): Library", restrictions.Output);
            }

            // AG006 applies to the configured source layer, even when its name is Application.
            var domainPolicy = Policy("error", "error", true).Replace("AG003: error", "AG006: error", StringComparison.Ordinal)
                .Replace("  - id: Application\n", "  - id: Application\n    domainInfrastructure:\n      forbiddenNamespaces: [Library]\n", StringComparison.Ordinal);
            await File.WriteAllTextAsync(policyPath, domainPolicy);
            var domainScan = await RunCli("scan", solution, "--policy", policyPath, "--trusted");
            Assert.True(domainScan.ExitCode == 1, domainScan.Output + domainScan.Error);
            Assert.Empty(domainScan.Error);
            Assert.Contains("error AG006:", domainScan.Output);
            Assert.Contains("layers.Application.domainInfrastructure.forbiddenNamespaces", domainScan.Output);
            Assert.Contains("Consumer.cs(1,", domainScan.Output);
            var domainInspect = await RunCli("inspect", solution, "--policy", policyPath, "--trusted");
            Assert.Equal(0, domainInspect.ExitCode);
            Assert.Contains("AG006 infrastructure namespaces (including descendants): Library", domainInspect.Output);
            await File.WriteAllTextAsync(policyPath, domainPolicy.Replace("forbiddenNamespaces: [Library]", "forbiddenNamespaces: [Library]\n      allowedNamespaces: [Library]", StringComparison.Ordinal));
            var exempted = await RunCli("scan", solution, "--policy", policyPath, "--trusted");
            Assert.True(exempted.ExitCode == 0, exempted.Output + exempted.Error);
            Assert.Contains("0 diagnostic(s)", exempted.Output);

            await File.WriteAllTextAsync(policyPath, Policy("error", "error", false));
            var first = await RunCli("scan", solution, "--policy", policyPath, "--trusted");
            var second = await RunCli("scan", solution, "--policy", policyPath, "--trusted");
            Assert.Equal(1, first.ExitCode);
            Assert.Equal(first.Output, second.Output);

            await File.AppendAllTextAsync(source, " public class Broken : MissingType { }");
            var incomplete = await RunCli("scan", solution, "--policy", policyPath, "--trusted");
            Assert.Equal(2, incomplete.ExitCode);
            Assert.Contains("AG001:", incomplete.Output);
            Assert.Contains("incomplete", incomplete.Error);
            Assert.Contains("MissingType", incomplete.Error);
            Assert.DoesNotContain("Gate passed", incomplete.Output);
            Assert.DoesNotContain("Gate failed", incomplete.Output);
            var incompleteInspect = await RunCli("inspect", solution, "--policy", policyPath, "--trusted");
            Assert.Equal(2, incompleteInspect.ExitCode);
            Assert.Contains("incomplete", incompleteInspect.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Checks actionable policy/trust errors and prevents invalid inputs from claiming coverage.</summary>
    [Fact]
    public async Task Configuration_and_trust_failures_return_two()
    {
        var directory = Path.Combine(Path.GetTempPath(), "archguard-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var policyPath = Path.Combine(directory, "policy.yaml");
            var missing = await RunCli("scan", "missing.csproj", "--policy", policyPath);
            Assert.Equal(2, missing.ExitCode);
            Assert.Contains("policy.yaml", missing.Error);
            Assert.Empty(missing.Output);

            await File.WriteAllTextAsync(policyPath, Policy("error", "error", false).Replace("version: 1", "version: 99", StringComparison.Ordinal));
            var invalid = await RunCli("inspect", "missing.csproj", "--policy", policyPath);
            Assert.Equal(2, invalid.ExitCode);
            Assert.Contains("policy.yaml", invalid.Error);
            Assert.Contains("version", invalid.Error);
            Assert.Empty(invalid.Output);

            await File.WriteAllTextAsync(policyPath, Policy("error", "error", false));
            var untrusted = await RunCli("scan", "missing.csproj", "--policy", policyPath);
            Assert.Equal(2, untrusted.ExitCode);
            Assert.Contains("trust", untrusted.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(untrusted.Output);

            var missingInput = await RunCli("scan", "missing.csproj", "--policy", policyPath, "--trusted");
            Assert.Equal(2, missingInput.ExitCode);
            Assert.NotEmpty(missingInput.Error);
            Assert.Empty(missingInput.Output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string Policy(string severity, string threshold, bool allowed) => $$"""
        version: 1
        modules:
          - id: App
            projects: ["App/*.csproj"]
            allowedDependencies: {{(allowed ? "[\"contract:Library/api\"]" : "[]")}}
          - id: Library
            projects: ["Library/*.csproj"]
            contracts:
              - id: api
                projects: ["Library/*.csproj"]
        layers:
          - id: Application
            projects: ["App/*.csproj"]
            allowedDependencies: {{(allowed ? "[Domain]" : "[]")}}
          - id: Domain
            projects: ["Library/*.csproj"]
        rules:
          AG001: {{severity}}
          AG002: {{severity}}
          AG003: error
        gate:
          failOn: {{threshold}}
        """;

    private static Task<CommandResult> RunCli(params string[] arguments)
    {
        var configuration = typeof(CliWorkspaceTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var assembly = Path.Combine(Repository.Root, "src", "ArchitectureGuardian.Cli", "bin", configuration,
            "net10.0", "ArchitectureGuardian.Cli.dll");
        return Repository.Dotnet(TimeSpan.FromSeconds(60), [assembly, .. arguments]);
    }
}
