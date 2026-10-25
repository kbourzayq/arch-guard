using System.Reflection;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using ArchitectureGuardian.Cli;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Cli;

/// <summary>Exercises policy-driven commands in process, retaining representative public process checks.</summary>
[Trait("Category", "Integration")]
[Collection(nameof(CliWorkspaceTests))]
public sealed class CliWorkspaceTests
{
    /// <summary>Checks findings, gates, partial coverage and inspection without repeatedly cold-starting the workspace host.</summary>
    [Fact]
    public async Task Scan_and_inspect_preserve_policy_and_coverage_contracts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "archguard-commands-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Combine(Repository.Root, "global.json"), Path.Combine(directory, "global.json"));
            Directory.CreateDirectory(Path.Combine(directory, "App"));
            Directory.CreateDirectory(Path.Combine(directory, "Library"));
            await File.WriteAllTextAsync(Path.Combine(directory, "App", "App.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                </Project>
                """, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "Library", "Library.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "Library", "Contract.cs"), "namespace Library; public class Contract { }", TestContext.Current.CancellationToken);
            var source = Path.Combine(directory, "App", "Consumer.cs");
            await File.WriteAllTextAsync(source, "public class Consumer : Library.Contract { }", TestContext.Current.CancellationToken);
            var solution = Path.Combine(directory, "Input.slnx");
            await File.WriteAllTextAsync(solution, "<Solution><Project Path=\"App/App.csproj\" /><Project Path=\"Library/Library.csproj\" /></Solution>", TestContext.Current.CancellationToken);
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
                await File.WriteAllTextAsync(policyPath, Policy(severity, threshold, allowed), TestContext.Current.CancellationToken);
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
                if (severity == "error" && !allowed)
                {
                    var process = await RunCliProcess("scan", solution, "--policy", policyPath, "--trusted",
                        "--configuration", "Release", "--framework", "net10.0");
                    Assert.Equal(result, process);
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
            Assert.Equal(inspect, await RunCliProcess("inspect", solution, "--policy", policyPath, "--trusted"));

            // Exercise compatible rule selections together; each command still loads the real workspace.
            var contractPolicy = Policy("error", "error", true)
                .Replace("AG003: error", "AG004: error\n  AG005: error\n  AG006: error", StringComparison.Ordinal);
            // AG004 rejects implementation access even with module permission; AG005/AG006 reject allowed references.
            var implementationPolicy = contractPolicy.Replace("contract:Library/api", "module:Library", StringComparison.Ordinal)
                .Replace("    contracts:\n      - id: api\n        projects: [\"Library/*.csproj\"]\n", "", StringComparison.Ordinal)
                .Replace("  - id: App\n", "  - id: App\n    forbiddenAssemblies: [Library]\n", StringComparison.Ordinal)
                .Replace("  - id: Application\n", "  - id: Application\n    domainInfrastructure:\n      forbiddenNamespaces: [Library]\n", StringComparison.Ordinal);
            await File.WriteAllTextAsync(policyPath, implementationPolicy, TestContext.Current.CancellationToken);
            var implementationScan = await RunCli("scan", solution, "--policy", policyPath, "--trusted");
            Assert.True(implementationScan.ExitCode == 1, implementationScan.Output + implementationScan.Error);
            Assert.Empty(implementationScan.Error);
            Assert.Contains("error AG004:", implementationScan.Output);
            Assert.Contains("error AG005:", implementationScan.Output);
            Assert.Contains("modules.App.forbiddenAssemblies restriction 'Library'", implementationScan.Output);
            Assert.Contains("error AG006:", implementationScan.Output);
            Assert.Contains("layers.Application.domainInfrastructure.forbiddenNamespaces", implementationScan.Output);
            Assert.Contains("symbol=Library.Contract", implementationScan.Output);
            Assert.Contains("Consumer.cs(1,", implementationScan.Output);
            Assert.DoesNotContain("AG001:", implementationScan.Output);
            Assert.DoesNotContain("AG002:", implementationScan.Output);

            // Both AG005 bans override an allowed contract, while AG006's namespace exception remains effective.
            var domainPolicy = contractPolicy
                .Replace("  - id: Application\n", "  - id: Application\n    domainInfrastructure:\n      forbiddenNamespaces: [Library]\n", StringComparison.Ordinal);
            var exemptedPolicy = domainPolicy.Replace("forbiddenNamespaces: [Library]", "forbiddenNamespaces: [Library]\n      allowedNamespaces: [Library]", StringComparison.Ordinal);
            var namespacePolicy = exemptedPolicy.Replace("  - id: App\n", "  - id: App\n    forbiddenAssemblies: [Library]\n    forbiddenNamespaces: [Library]\n", StringComparison.Ordinal);
            await File.WriteAllTextAsync(policyPath, namespacePolicy, TestContext.Current.CancellationToken);
            var restricted = await RunCli("scan", solution, "--policy", policyPath, "--trusted");
            Assert.True(restricted.ExitCode == 1, restricted.Output + restricted.Error);
            Assert.Empty(restricted.Error);
            Assert.Contains("error AG005:", restricted.Output);
            Assert.Contains("modules.App.forbiddenAssemblies restriction 'Library'", restricted.Output);
            Assert.Contains("modules.App.forbiddenNamespaces restriction 'Library'", restricted.Output);
            Assert.Contains("symbol=Library.Contract", restricted.Output);
            Assert.Contains("Consumer.cs(1,", restricted.Output);
            foreach (var rule in new[] { "AG001:", "AG002:", "AG004:", "AG006:" }) { Assert.DoesNotContain(rule, restricted.Output); }
            var restrictions = await RunCli("inspect", solution, "--policy", policyPath, "--trusted");
            Assert.Equal(0, restrictions.ExitCode);
            Assert.Contains("Forbidden assemblies: Library", restrictions.Output);
            Assert.Contains("Forbidden namespaces (including descendants): Library", restrictions.Output);
            Assert.Contains("AG006 infrastructure namespaces (including descendants): Library", restrictions.Output);

            // Project input also includes its reference and preserves contract/exception acceptance.
            await File.WriteAllTextAsync(policyPath, exemptedPolicy, TestContext.Current.CancellationToken);
            var exempted = await RunCli("scan", Path.Combine(directory, "App", "App.csproj"), "--policy", policyPath, "--trusted");
            Assert.True(exempted.ExitCode == 0, exempted.Output + exempted.Error);
            Assert.Contains("0 diagnostic(s)", exempted.Output);

            await File.WriteAllTextAsync(policyPath, Policy("error", "error", false), TestContext.Current.CancellationToken);

            // Baseline writes are explicit; source movement preserves debt while a newly selected restriction blocks.
            var baselinePath = Path.Combine(directory, "baseline.json");
            var update = await RunCli("baseline", "update", solution, "--policy", policyPath, "--baseline", baselinePath, "--trusted");
            Assert.True(update.ExitCode == 0, update.Output + update.Error);
            Assert.Contains("Baseline updated: 2 accepted", update.Output);
            // These two baseline commands independently scan the same source and policy. Compare
            // their entire findings block instead of doing two additional scans only for repeatability.
            var summary = update.Output.IndexOf("Baseline updated:", StringComparison.Ordinal);
            Assert.True(summary > 0);
            var findings = update.Output[..summary];
            var baselineBytes = await File.ReadAllBytesAsync(baselinePath, TestContext.Current.CancellationToken);
            var accepted = await RunCli("scan", solution, "--policy", policyPath, "--baseline", baselinePath, "--trusted");
            Assert.True(accepted.ExitCode == 0, accepted.Output + accepted.Error);
            Assert.Contains("2 existing, 0 new, 0 resolved", accepted.Output);
            Assert.Contains("error AG001:", accepted.Output);
            Assert.Contains("Existing:", accepted.Output);
            Assert.StartsWith(findings, accepted.Output);

            var moved = Path.Combine(directory, "App", "Renamed.cs");
            File.Move(source, moved);
            source = moved;
            await File.WriteAllTextAsync(source, "\n\npublic class Consumer : Library.Contract { }", TestContext.Current.CancellationToken);
            var movedScan = await RunCli("scan", solution, "--policy", policyPath, "--baseline", baselinePath, "--trusted");
            Assert.True(movedScan.ExitCode == 0, movedScan.Output + movedScan.Error);
            Assert.Contains("2 existing, 0 new, 0 resolved", movedScan.Output);
            Assert.Contains("Renamed.cs(3,", movedScan.Output);
            Assert.Equal(baselineBytes, await File.ReadAllBytesAsync(baselinePath, TestContext.Current.CancellationToken));

            var newRestriction = Policy("error", "error", false)
                .Replace("AG003: error", "AG003: error\n  AG005: error", StringComparison.Ordinal)
                .Replace("  - id: App\n", "  - id: App\n    forbiddenNamespaces: [Library]\n", StringComparison.Ordinal);
            await File.WriteAllTextAsync(policyPath, newRestriction, TestContext.Current.CancellationToken);
            var newScan = await RunCli("scan", solution, "--policy", policyPath, "--baseline", baselinePath, "--trusted");
            Assert.True(newScan.ExitCode == 1, newScan.Output + newScan.Error);
            Assert.Contains("2 existing, 1 new, 0 resolved", newScan.Output);
            Assert.Contains("New:", newScan.Output);
            Assert.Contains("Gate failed", newScan.Output);
            Assert.Equal(baselineBytes, await File.ReadAllBytesAsync(baselinePath, TestContext.Current.CancellationToken));

            await File.WriteAllTextAsync(policyPath, Policy("error", "error", true), TestContext.Current.CancellationToken);
            var resolved = await RunCli("scan", solution, "--policy", policyPath, "--baseline", baselinePath, "--trusted");
            Assert.True(resolved.ExitCode == 0, resolved.Output + resolved.Error);
            Assert.Contains("0 existing, 0 new, 2 resolved", resolved.Output);
            Assert.Contains("Resolved:", resolved.Output);
            await File.WriteAllTextAsync(policyPath, Policy("error", "error", false), TestContext.Current.CancellationToken);

            await File.AppendAllTextAsync(source, " public class Broken : MissingType { }", TestContext.Current.CancellationToken);
            var incompleteBaseline = await RunCli("scan", solution, "--policy", policyPath, "--baseline", baselinePath, "--trusted");
            Assert.Equal(2, incompleteBaseline.ExitCode);
            Assert.Contains("incomplete", incompleteBaseline.Error);
            Assert.DoesNotContain("Gate passed", incompleteBaseline.Output);
            Assert.DoesNotContain("resolved", incompleteBaseline.Output);
            var incompleteUpdate = await RunCli("baseline", "update", solution, "--policy", policyPath, "--baseline", baselinePath, "--trusted");
            Assert.Equal(2, incompleteUpdate.ExitCode);
            Assert.DoesNotContain("Baseline updated", incompleteUpdate.Output);
            Assert.Equal(baselineBytes, await File.ReadAllBytesAsync(baselinePath, TestContext.Current.CancellationToken));

            // A valid accepted inventory cannot mask an input-loading failure either.
            var missingInput = Path.Combine(directory, "Missing.slnx");
            foreach (var command in new[] { new[] { "scan" }, new[] { "baseline", "update" } })
            {
                var missing = await RunCli([.. command, missingInput, "--policy", policyPath, "--baseline", baselinePath, "--trusted"]);
                Assert.Equal(2, missing.ExitCode);
                Assert.NotEmpty(missing.Error);
                Assert.DoesNotContain("Gate passed", missing.Output);
                Assert.DoesNotContain("Baseline updated", missing.Output);
                Assert.Equal(baselineBytes, await File.ReadAllBytesAsync(baselinePath, TestContext.Current.CancellationToken));
            }

            var incomplete = await RunCliProcess("scan", solution, "--policy", policyPath, "--trusted");
            Assert.Equal(2, incomplete.ExitCode);
            Assert.Contains("AG001:", incomplete.Output);
            Assert.Contains("incomplete", incomplete.Error);
            Assert.Contains("MissingType", incomplete.Error);
            Assert.DoesNotContain("Gate passed", incomplete.Output);
            Assert.DoesNotContain("Gate failed", incomplete.Output);
            // Cold-process inspection is already compared above; exercise partial coverage in process.
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

            await File.WriteAllTextAsync(policyPath, Policy("error", "error", false).Replace("version: 1", "version: 99", StringComparison.Ordinal), TestContext.Current.CancellationToken);
            var invalid = await RunCli("inspect", "missing.csproj", "--policy", policyPath);
            Assert.Equal(2, invalid.ExitCode);
            Assert.Contains("policy.yaml", invalid.Error);
            Assert.Contains("version", invalid.Error);
            Assert.Empty(invalid.Output);

            await File.WriteAllTextAsync(policyPath, Policy("error", "error", false), TestContext.Current.CancellationToken);
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

    private static async Task<CommandResult> RunCli(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var exitCode = await CliApplication.RunAsync(arguments, output, error, timeout.Token);
        return new(exitCode, output.ToString(), error.ToString());
    }

    private static Task<CommandResult> RunCliProcess(params string[] arguments)
    {
        var configuration = typeof(CliWorkspaceTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var assembly = Path.Combine(Repository.Root, "src", "ArchitectureGuardian.Cli", "bin", configuration,
            "net10.0", "ArchitectureGuardian.Cli.dll");
        return Repository.Dotnet(TimeSpan.FromSeconds(60), [assembly, .. arguments]);
    }
}
