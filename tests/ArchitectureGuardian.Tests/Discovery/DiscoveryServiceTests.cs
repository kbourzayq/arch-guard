using System.Xml.Linq;
using ArchitectureGuardian.Analysis.Discovery;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Discovery;
using ArchitectureGuardian.Tests.Workspaces;
using Xunit;

namespace ArchitectureGuardian.Tests.Discovery;

/// <summary>Verifies policy-free discovery through restored real workspaces and failure fixtures.</summary>
/// <param name="fixture">Isolated workspace inputs, including generators and intentionally broken projects.</param>
[Trait("Category", "Integration")]
public sealed class DiscoveryServiceTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    /// <summary>Loads actual assembly identities and semantic evidence without reading the existing approved policy.</summary>
    [Fact]
    public async Task Modular_inventory_proposes_intentions_without_legalizing_observed_edges()
    {
        var root = Path.Combine(fixture.Root, "ModularMonolith");
        var policyPath = Path.Combine(root, "architecture.guardian.yaml");
        // A malformed approved policy proves discovery does not depend on policy loading or activation.
        var original = await File.ReadAllTextAsync(policyPath, TestContext.Current.CancellationToken);
        try
        {
            await File.WriteAllTextAsync(policyPath, "not: an approved policy", TestContext.Current.CancellationToken);
            var result = await new DiscoveryService().DiscoverAsync(new(Path.Combine(root, "ModularMonolith.slnx"), true, "Release"), root, TestContext.Current.CancellationToken);
            AssertComplete(result);
            Assert.Equal(5, result.Observations.Inventory!.Projects.Count);
            Assert.Equal("Company.Billing.Messages", result.Observations.Inventory.Projects.Single(project => project.Name == "Billing.Contracts").Assembly.Name);
            Assert.Equal(5, result.Contexts.Count);
            Assert.All(result.Contexts, context => { Assert.Equal("Release", context.Configuration); Assert.Equal("net10.0", context.Framework); });
            Assert.NotNull(result.Proposal.Policy);
            Assert.Contains(result.Compliance!.Diagnostics, diagnostic => diagnostic.RuleId == "AG001");
            Assert.Contains(result.Observations.Graph!.Edges, edge => edge.Kind == DependencyKind.Project && edge.Evidence.Any(evidence => evidence.ProjectReference is not null));
            Assert.Contains(result.Observations.Graph.Nodes, node => node.Kind == DependencyKind.Assembly && node.ProjectPath is null);
            Assert.Equal("not: an approved policy", await File.ReadAllTextAsync(policyPath, TestContext.Current.CancellationToken));
        }
        finally { await File.WriteAllTextAsync(policyPath, original, CancellationToken.None); }
    }

    /// <summary>All supported input formats retain both direct and referenced projects without ownership assumptions.</summary>
    /// <param name="input">Supported fixture input.</param>
    [Theory]
    [InlineData("Fixture.sln")]
    [InlineData("Fixture.slnx")]
    [InlineData("App/App.csproj")]
    public async Task Supported_inputs_inventory_references_and_leave_unrecognized_roles_unresolved(string input)
    {
        var result = await DiscoverAsync(input);
        AssertComplete(result);
        Assert.Equal(new[] { "App/App.csproj", "Library/Library.csproj" }, result.Observations.Inventory!.Projects.Select(project => project.Id));
        Assert.Equal(result.StaticProjects, result.Observations.Inventory.Projects.Select(project => project.Id));
        Assert.Null(result.Proposal.Policy);
        Assert.NotEmpty(result.Proposal.Questions);
    }

    /// <summary>Records framework choices separately from the one explored context and preserves conditional declarations.</summary>
    [Fact]
    public async Task Contexts_do_not_claim_unexplored_frameworks_or_configurations()
    {
        var implicitTarget = await DiscoverAsync("Multi/Multi.csproj");
        Assert.Equal(AnalysisStatus.Incomplete, implicitTarget.Status);
        Assert.Contains(implicitTarget.Observations.Issues, issue => issue.Code == "framework-required");
        Assert.Null(implicitTarget.Proposal.Policy);
        var selected = await DiscoverAsync("Multi/Multi.csproj", framework: "netstandard2.1");
        AssertComplete(selected);
        var context = Assert.Single(selected.Contexts);
        Assert.Equal("netstandard2.1", context.Framework);
        Assert.Equal(new[] { "net10.0", "netstandard2.1" }, context.DeclaredFrameworks);
        var debug = await DiscoverAsync("App/App.csproj");
        var release = await DiscoverAsync("App/App.csproj", "Release");
        AssertComplete(debug);
        AssertComplete(release);
        Assert.Contains(debug.Observations.Inventory!.Projects.SelectMany(project => project.Assembly.Namespaces).SelectMany(ns => ns.Types), type => type.MetadataName == "App.DebugContract");
        Assert.Contains(release.Observations.Inventory!.Projects.SelectMany(project => project.Assembly.Namespaces).SelectMany(ns => ns.Types), type => type.MetadataName == "App.ReleaseContract");
        Assert.All(debug.Contexts, item => Assert.Equal("Debug", item.Configuration));
        Assert.All(release.Contexts, item => Assert.Equal("Release", item.Configuration));
    }

    /// <summary>Unrestored and unresolved-reference inputs cannot produce complete coverage or a policy.</summary>
    /// <param name="input">Intentionally unavailable fixture context.</param>
    /// <param name="code">Required operational issue.</param>
    [Theory]
    [InlineData("FailedRestore/FailedRestore.csproj", "restore-required")]
    [InlineData("MissingReference/MissingReference.csproj", "workspace-load-failed")]
    [InlineData("PartialReference/PartialReference.csproj", "reference-unavailable")]
    public async Task Missing_context_remains_incomplete(string input, string code)
    {
        var result = await DiscoverAsync(input);
        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Contains(result.Observations.Issues, issue => issue.Code == code);
        Assert.Null(result.Proposal.Policy);
    }

    /// <summary>Includes source-generated namespaces and scoped generated-file identities in neutral evidence.</summary>
    [Fact]
    public async Task Generated_declarations_are_not_silently_omitted()
    {
        var result = await DiscoverAsync("GeneratorConsumer/GeneratorConsumer.csproj");
        AssertComplete(result);
        Assert.Contains(result.Observations.Inventory!.Projects.SelectMany(project => project.Assembly.Namespaces).SelectMany(ns => ns.Types), type => type.GeneratedFile is not null);
        Assert.NotEmpty(result.Observations.Graph!.Edges);
    }

    /// <summary>Inactive literal references remain static hints, while actual evaluated projects still enforce root boundaries.</summary>
    [Fact]
    public async Task Inactive_outside_and_malformed_references_do_not_block_verified_contexts()
    {
        var root = Path.Combine(fixture.Root, "DiscoveryConditional");
        Directory.CreateDirectory(root);
        var outside = Path.Combine(fixture.Root, "InactiveOutside");
        Directory.CreateDirectory(outside);
        var project = Path.Combine(root, "Shop.Domain.csproj");
        var outsideProject = Path.Combine(outside, "Inactive.csproj");
        await File.WriteAllTextAsync(outsideProject, "<Project>", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "Broken.csproj"), "<Project>", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "Marker.cs"), "namespace Shop.Domain; public class Marker { }", TestContext.Current.CancellationToken);
        var xml = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup Condition="'$(Configuration)' == 'Never'">
                <ProjectReference Include="../InactiveOutside/Inactive.csproj" />
                <ProjectReference Include="Broken.csproj" />
                <ProjectReference Include="Missing.csproj" />
              </ItemGroup>
            </Project>
            """;
        await File.WriteAllTextAsync(project, xml, TestContext.Current.CancellationToken);
        await fixture.RestoreAsync(project);
        var result = await new DiscoveryService().DiscoverAsync(new(project, true, "Release"), root, TestContext.Current.CancellationToken);
        AssertComplete(result);
        Assert.NotNull(result.Proposal.Policy);
        Assert.Single(result.Contexts);
        Assert.Equal("Shop.Domain.csproj", Assert.Single(result.Observations.Inventory!.Projects).Id);
        Assert.Equal(4, result.StaticProjects.Count);
        Assert.Equal(new[] { "static-project-outside-root", "static-project-unavailable", "static-project-unreadable" }, result.StaticIssues.Select(issue => issue.Code));
        Assert.Empty(result.Observations.Issues);

        // When the outside reference becomes active, evaluated normalization must still reject it.
        await File.WriteAllTextAsync(outsideProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../InactiveOutside/Inactive.csproj\" /></ItemGroup></Project>", TestContext.Current.CancellationToken);
        await fixture.RestoreAsync(project);
        var active = await new DiscoveryService().DiscoverAsync(new(project, true, "Release"), root, TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisStatus.Incomplete, active.Status);
        Assert.Null(active.Proposal.Policy);
        Assert.Contains(active.Observations.Issues, issue => issue.Code == "repository-mapping-invalid");
    }

    /// <summary>Denies evaluation before any repository task or property function can execute.</summary>
    [Fact]
    public async Task Denied_trust_leaves_msbuild_sentinel_untouched()
    {
        var sentinel = Path.Combine(fixture.Root, "executed.txt");
        var targets = Path.Combine(fixture.Root, "Directory.Build.targets");
        new XDocument(new XElement("Project", new XElement("PropertyGroup", new XElement("Sentinel",
            "$([System.IO.File]::WriteAllText('" + sentinel + "', 'executed'))")))).Save(targets);
        try
        {
            var result = await new DiscoveryService().DiscoverAsync(new(Path.Combine(fixture.Root, "Fixture.slnx")), fixture.Root, TestContext.Current.CancellationToken);
            Assert.Equal(AnalysisStatus.Incomplete, result.Status);
            Assert.Empty(result.Contexts);
            Assert.Equal(2, result.StaticProjects.Count);
            Assert.Contains(result.Observations.Issues, issue => issue.Code == "workspace-untrusted");
            Assert.Null(result.Proposal.Policy);
            Assert.False(File.Exists(sentinel));
        }
        finally { File.Delete(targets); }
    }

    /// <summary>Cancellation and malformed input retain their distinct outcomes and never invent a proposal.</summary>
    [Fact]
    public async Task Cancelled_and_failed_discovery_remain_explicit()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var cancelled = await new DiscoveryService().DiscoverAsync(new(Path.Combine(fixture.Root, "Fixture.slnx"), true), fixture.Root, source.Token);
        Assert.Equal(AnalysisStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.Proposal.Policy);
        var failed = await DiscoverAsync("does-not-exist.csproj");
        Assert.Equal(AnalysisStatus.Incomplete, failed.Status);
        Assert.Null(failed.Proposal.Policy);
        var malformed = Path.Combine(fixture.Root, "Malformed.slnx");
        await File.WriteAllTextAsync(malformed, "<Solution>", TestContext.Current.CancellationToken);
        var invalid = await DiscoverAsync("Malformed.slnx");
        Assert.Equal(AnalysisStatus.Failed, invalid.Status);
        Assert.Contains(invalid.Observations.Issues, issue => issue.Code == "discovery-input-failed");
        Assert.Null(invalid.Proposal.Policy);
    }

    private Task<DiscoveryResult> DiscoverAsync(string input, string configuration = "Debug", string? framework = null) =>
        new DiscoveryService().DiscoverAsync(new(Path.Combine(fixture.Root, input), true, configuration, framework), fixture.Root, TestContext.Current.CancellationToken);

    private static void AssertComplete(DiscoveryResult result) => Assert.True(result.Status == AnalysisStatus.Completed,
        string.Join(Environment.NewLine, result.Observations.Issues.Select(issue => issue.Code + ": " + issue.Message)));
}
