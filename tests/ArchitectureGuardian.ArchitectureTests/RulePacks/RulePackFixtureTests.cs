using System.Reflection;
using ArchitectureGuardian.Analysis.Dependencies;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.RulePacks;

/// <summary>Scans compliant and violating solutions that adopt each built-in rule pack through <c>extends</c>.</summary>
/// <param name="fixture">Isolated checked-in solutions and restored contexts.</param>
[Trait("Category", "Integration")]
[Collection(nameof(RulePackFixtureTests))]
public sealed class RulePackFixtureTests(RulePackFixture fixture) : IClassFixture<RulePackFixture>
{
    /// <summary>Each labeled configuration yields exactly the expected rules, at the scenario's source file, and gate.</summary>
    /// <param name="solution">The fixture solution and pack layout.</param>
    /// <param name="configuration">The committed scenario selected by MSBuild.</param>
    /// <param name="expectedRules">The comma-separated distinct rule IDs expected, or empty for a compliant case.</param>
    /// <param name="scenarioFile">The source file containing every expected finding, or null for a compliant case.</param>
    [Theory]
    [InlineData("CleanArchitecture", "Compliant", "", null)]
    [InlineData("CleanArchitecture", "AG002", "AG002", "src/Shop.Application/Checkout.cs")]
    [InlineData("CleanArchitecture", "AG006", "AG006", "src/Shop.Domain/Order.cs")]
    [InlineData("ModularMonolith", "Compliant", "", null)]
    [InlineData("ModularMonolith", "AG003", "AG003", "src/Modules/Billing/Billing.Application/InvoiceCalculator.cs")]
    [InlineData("ModularMonolith", "AG004", "AG001,AG004", "src/Modules/Orders/Orders.Application/Checkout.cs")]
    [InlineData("VerticalSlice", "Compliant", "", null)]
    [InlineData("VerticalSlice", "AG004", "AG001,AG004", "src/Features/Ordering/Ordering.Feature/PlaceOrder.cs")]
    public async Task Pack_fixtures_match_expected_outcomes(string solution, string configuration, string expectedRules,
        string? scenarioFile)
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(fixture.Root, solution);
        var input = Path.Combine(root, solution + ".slnx");
        var policyPath = Path.Combine(root, "architecture.guardian.yaml");
        var policy = await new YamlPolicyLoader().LoadAsync(policyPath, token);
        var rules = policy.SelectRules<DependencyBuildResult>([new ModuleDependenciesRule(), new LayerDependenciesRule(),
            new ModuleCyclesRule(), new ImplementationBoundariesRule(), new ForbiddenReferencesRule(), new DomainInfrastructureRule()]);
        DependencyBuildResult context;
        using (var workspace = await new MSBuildWorkspaceLoader().LoadAsync(new(input, isTrusted: true, configuration: configuration), token))
        {
            context = await new DependencyGraphBuilder().BuildAsync(workspace, root, policy, token);
        }

        var result = await new AnalysisService<DependencyBuildResult>(rules).AnalyzeAsync(new(context, policy.SeverityPolicy), token);
        Assert.Equal(AnalysisStatus.Completed, result.Status);
        Assert.Empty(result.Issues);
        var expected = expectedRules.Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expected, result.Diagnostics.Select(item => item.RuleId).Distinct().Order(StringComparer.Ordinal));
        Assert.All(result.Diagnostics, item =>
        {
            Assert.Equal(DiagnosticSeverity.Error, item.Severity);
            Assert.Equal(scenarioFile, item.Location?.Path);
        });
        Assert.Equal(expected.Length == 0, policy.Gate.Passes(result));

        var buildConfiguration = typeof(RulePackFixtureTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var cli = Path.Combine(Repository.Root, "src", "ArchitectureGuardian.Cli", "bin", buildConfiguration, "net10.0",
            "ArchitectureGuardian.Cli.dll");
        var scan = await Repository.Dotnet(TimeSpan.FromSeconds(60), cli, "scan", input, "--policy", policyPath,
            "--configuration", configuration, "--trusted");
        Assert.True(scan.ExitCode == (expected.Length == 0 ? 0 : 1), scan.Output + scan.Error);
        Assert.Empty(scan.Error);
        Assert.Contains(expected.Length == 0 ? "Gate passed" : "Gate failed", scan.Output);
        foreach (var diagnostic in result.Diagnostics)
        {
            Assert.Contains("ID: " + diagnostic.Id, scan.Output);
        }
    }
}
