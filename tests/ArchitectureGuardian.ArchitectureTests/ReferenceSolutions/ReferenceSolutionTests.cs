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

namespace ArchitectureGuardian.ArchitectureTests.ReferenceSolutions;

/// <summary>Uses labeled positive and negative cases as an end-to-end precision/recall regression set.</summary>
/// <param name="fixture">Isolated checked-in solutions and restored contexts.</param>
[Trait("Category", "Integration")]
[Collection(nameof(ReferenceSolutionTests))]
public sealed class ReferenceSolutionTests(ReferenceSolutionFixture fixture) : IClassFixture<ReferenceSolutionFixture>
{
    /// <summary>Asserts the entire finding set, evidence, stable identities and public CLI gate for each labeled case.</summary>
    /// <param name="solution">The reference architecture.</param>
    /// <param name="configuration">The committed scenario selected by MSBuild.</param>
    /// <param name="expectedRule">The sole expected finding, or null for a compliant case.</param>
    /// <param name="expectedExit">The CLI gate or coverage exit code.</param>
    [Theory]
    [InlineData("CleanArchitecture", "Compliant", null, 0)]
    [InlineData("CleanArchitecture", "AG002", "AG002", 1)]
    [InlineData("CleanArchitecture", "Incomplete", "AG002", 2)]
    [InlineData("ModularMonolith", "Compliant", null, 0)]
    [InlineData("ModularMonolith", "AG001", "AG001", 1)]
    [InlineData("ModularMonolith", "AG003", "AG003", 1)]
    [InlineData("ModularMonolith", "Incomplete", "AG001", 2)]
    public async Task Labeled_cases_match_exact_findings_and_gate(string solution, string configuration,
        string? expectedRule, int expectedExit)
    {
        var root = Path.Combine(fixture.Root, solution);
        var input = Path.Combine(root, solution + ".slnx");
        var policyPath = Path.Combine(root, "architecture.guardian.yaml");
        var policy = await new YamlPolicyLoader().LoadAsync(policyPath, TestContext.Current.CancellationToken);
        var rules = policy.SelectRules<DependencyBuildResult>(
            [new ModuleDependenciesRule(), new LayerDependenciesRule(), new ModuleCyclesRule()]);
        var service = new AnalysisService<DependencyBuildResult>(rules);

        async Task<AnalysisResult> AnalyzeAsync()
        {
            using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(new(input, isTrusted: true, configuration: configuration));
            if (solution == "ModularMonolith" && configuration == "Incomplete")
            {
                Assert.Equal(AnalysisStatus.Incomplete, workspace.Status);
                Assert.Contains(workspace.Issues, issue => issue.Message.Contains("Unavailable", StringComparison.Ordinal));
            }

            var context = await new DependencyGraphBuilder().BuildAsync(workspace, root, policy);
            return await service.AnalyzeAsync(new(context, policy.SeverityPolicy));
        }

        var first = await AnalyzeAsync();
        var second = await AnalyzeAsync();
        Assert.Equal(expectedExit == 2 ? AnalysisStatus.Incomplete : AnalysisStatus.Completed, first.Status);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Diagnostics.Select(item => item.Id), second.Diagnostics.Select(item => item.Id));
        Assert.Equal(first.Issues, second.Issues);
        Assert.Equal(expectedExit == 0, policy.Gate.Passes(first));
        if (expectedExit == 2)
        {
            Assert.NotEmpty(first.Issues);
        }
        else
        {
            Assert.Empty(first.Issues);
        }

        // Exact cardinality catches false positives as well as missed violations. Every case selects all three rules.
        if (expectedRule is null)
        {
            Assert.Empty(first.Diagnostics);
        }
        else
        {
            var diagnostic = Assert.Single(first.Diagnostics);
            Assert.Equal(expectedRule, diagnostic.RuleId);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.NotEmpty(diagnostic.Evidence);
            Assert.NotNull(diagnostic.Location);
            Assert.Contains(diagnostic.Evidence, fact => fact.Location == diagnostic.Location);
            Assert.All(diagnostic.Evidence.Where(fact => fact.Location is not null), fact =>
            {
                var location = fact.Location!;
                Assert.False(Path.IsPathFullyQualified(location.Path));
                var lines = File.ReadAllLines(Path.Combine(root, location.Path));
                Assert.InRange(location.StartLine, 1, lines.Length);
                Assert.InRange(location.StartColumn, 1, lines[location.StartLine - 1].Length);
                Assert.InRange(location.EndLine, location.StartLine, lines.Length);
                Assert.InRange(location.EndColumn, 1, lines[location.EndLine - 1].Length + 1);
            });
            switch (expectedRule)
            {
                case "AG001":
                    Assert.Equal("Orders", diagnostic.Target.Module);
                    Assert.Contains("Billing.Application/Billing.Application.csproj", diagnostic.Message);
                    AssertSource(diagnostic, "Orders.Application/Checkout.cs", 8, "Billing.Application.InvoiceService");
                    break;
                case "AG002":
                    Assert.Equal("Shop", diagnostic.Target.Module);
                    Assert.Contains("Shop.Application/Shop.Application.csproj", diagnostic.Message);
                    Assert.Contains("Shop.Infrastructure/Shop.Infrastructure.csproj", diagnostic.Message);
                    AssertSource(diagnostic, "Shop.Application/Checkout.cs", 7, "Shop.Infrastructure.OrderStore");
                    break;
                case "AG003":
                    Assert.Equal("Billing", diagnostic.Target.Module);
                    Assert.Contains("Billing → Orders → Billing", diagnostic.Message);
                    AssertSource(diagnostic, "Billing.Application/InvoiceService.cs", 7, "Orders.Contracts.IOrder");
                    AssertSource(diagnostic, "Orders.Application/Checkout.cs", 6, "Billing.Contracts.IInvoice");
                    break;
            }
        }

        var buildConfiguration = typeof(ReferenceSolutionTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var cli = Path.Combine(Repository.Root, "src", "ArchitectureGuardian.Cli", "bin", buildConfiguration,
            "net10.0", "ArchitectureGuardian.Cli.dll");
        var scan = await Repository.Dotnet(TimeSpan.FromSeconds(60), cli, "scan", input, "--policy", policyPath,
            "--configuration", configuration, "--trusted");
        Assert.True(scan.ExitCode == expectedExit, scan.Output + scan.Error);
        foreach (var diagnostic in first.Diagnostics)
        {
            Assert.Contains("ID: " + diagnostic.Id, scan.Output);
        }
        Assert.Equal(first.Diagnostics.Count, scan.Output.Split("  ID: ", StringSplitOptions.None).Length - 1);
        if (expectedExit == 2)
        {
            Assert.Contains("incomplete", scan.Error);
            Assert.DoesNotContain("Gate passed", scan.Output);
            Assert.DoesNotContain("Gate failed", scan.Output);
        }
        else
        {
            Assert.Empty(scan.Error);
            Assert.Contains(expectedExit == 0 ? "Gate passed" : "Gate failed", scan.Output);
        }
    }

    private static void AssertSource(Diagnostic diagnostic, string path, int line, string symbol)
    {
        Assert.Contains(diagnostic.Evidence, fact => fact.Location?.Path == path && fact.Location.StartLine == line
            && fact.Description.Contains(symbol, StringComparison.Ordinal));
    }
}
