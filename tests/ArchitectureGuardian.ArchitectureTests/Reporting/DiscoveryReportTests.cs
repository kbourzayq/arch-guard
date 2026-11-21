using System.Text.Json;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Discovery;
using ArchitectureGuardian.Core.Repositories;
using ArchitectureGuardian.Reporting;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Reporting;

/// <summary>Verifies that machine discovery reports retain noncompleted outcomes without an available proposal.</summary>
public sealed class DiscoveryReportTests
{
    /// <summary>Reports unavailable context without converting it into configuration validity or compliance.</summary>
    /// <param name="status">Noncompleted coverage.</param>
    /// <param name="expected">Version-one wire spelling.</param>
    [Theory]
    [InlineData(AnalysisStatus.Incomplete, "incomplete")]
    [InlineData(AnalysisStatus.Failed, "failed")]
    [InlineData(AnalysisStatus.Cancelled, "cancelled")]
    public void Noncompleted_reports_preserve_coverage_and_unresolved_intentions(AnalysisStatus status, string expected)
    {
        var observations = new DependencyInventoryResult(null, null, status,
            status == AnalysisStatus.Cancelled ? [] : [new("unavailable", "Resolve the missing context.")]);
        var proposal = new PolicyProposalBuilder().Build(observations, TestContext.Current.CancellationToken);
        var result = new DiscoveryResult("App.slnx", "Release", null, [], [], observations, proposal);
        var text = DiscoveryReportSerializer.Serialize(result);
        Assert.EndsWith("\n", text);
        Assert.DoesNotContain("\r", text);
        using var report = JsonDocument.Parse(text);
        Assert.Equal(expected, report.RootElement.GetProperty("status").GetString());
        Assert.Equal("unresolved", report.RootElement.GetProperty("proposalOutcome").GetString());
        Assert.Equal("notAvailable", report.RootElement.GetProperty("configurationValidation").GetString());
        Assert.Equal(JsonValueKind.Null, report.RootElement.GetProperty("proposedPolicy").ValueKind);
        Assert.Equal(JsonValueKind.Null, report.RootElement.GetProperty("compliance").ValueKind);
        Assert.True(report.RootElement.GetProperty("reviewRequired").GetBoolean());
        Assert.Equal("manual", report.RootElement.GetProperty("adoption").GetString());
    }

    /// <summary>ABP solutions report the abp pack suggestion and settings; other proposals keep their established shape.</summary>
    /// <param name="abpVersion">The referenced ABP assembly version, or null for a solution without ABP.</param>
    [Theory]
    [InlineData("10.7.1.0")]
    [InlineData("9.3.0.0")]
    [InlineData(null)]
    public void Abp_proposals_report_pack_and_settings(string? abpVersion)
    {
        string[] names = abpVersion is null ? ["Shop.Domain", "Shop.Web"] : ["Acme.Shop.Domain", "Acme.Shop.Web"];
        var projects = names.Select(name => new RepositoryProject($"src/{name}/{name}.csproj", name, new RepositoryAssembly($"src/{name}/{name}.csproj", name,
            [new RepositoryNamespace($"src/{name}/{name}.csproj", name, [new RepositoryType($"src/{name}/{name}.csproj", name, "Item", RepositoryTypeKind.Class, true)])]))).ToArray();
        DependencyEdge[] edges = abpVersion is null ? [] : projects.Select(project => new DependencyEdge(
            new DependencyNode(DependencyKind.Assembly, project.Id, project.Name, project.Id),
            new DependencyNode(DependencyKind.Assembly, "external:Volo.Abp.Ddd.Domain", $"Volo.Abp.Ddd.Domain, Version={abpVersion}, Culture=neutral, PublicKeyToken=null"),
            [new DependencyEvidence(project.Id, null, new("references ABP"))])).ToArray();
        var observations = new DependencyInventoryResult(new RepositoryInventory("App.slnx", projects),
            new DependencyGraph(edges.SelectMany(edge => new[] { edge.Source, edge.Target }).DistinctBy(node => node.Id), edges), AnalysisStatus.Completed);
        var proposal = new PolicyProposalBuilder().Build(observations, TestContext.Current.CancellationToken);
        using var report = JsonDocument.Parse(DiscoveryReportSerializer.Serialize(new DiscoveryResult("App.slnx", "Release", null, [], [], observations, proposal)));
        var policy = report.RootElement.GetProperty("proposedPolicy");
        if (abpVersion is null)
        {
            Assert.Null(proposal.Pack);
            Assert.False(policy.TryGetProperty("abp", out _));
            return;
        }

        Assert.Equal(new[] { "layer:ApplicationContracts", "layer:Application", "layer:DomainShared", "layer:HttpApiClient", "layer:HttpApi", "layer:Persistence", "layer:Tests" }
            .Order(StringComparer.Ordinal), proposal.Pack!.Exclusions);
        Assert.Equal(["Domain"], policy.GetProperty("abp").GetProperty("repositoryLayers").EnumerateArray().Select(item => item.GetString()));
        Assert.True(policy.GetProperty("rules").TryGetProperty("ABP002", out _));
        Assert.Contains(report.RootElement.GetProperty("inferences").EnumerateArray(), item => item.GetProperty("kind").GetString() == "rule-pack");
        // Versions other than the verified major are proposed with an explicit review question.
        Assert.Equal(abpVersion.StartsWith("9.", StringComparison.Ordinal),
            proposal.Questions.Any(question => question.Contains("references ABP 9.3.0", StringComparison.Ordinal)));
    }

    /// <summary>An ABP solution without repository-bearing layers still gets a proposal, with ABP003 excluded rather than failing.</summary>
    [Fact]
    public void Abp_proposals_without_repository_layers_exclude_abp003()
    {
        string[] names = ["Acme.Shop.Application.Contracts", "Acme.Shop.HttpApi.Client"];
        var projects = names.Select(name => new RepositoryProject($"src/{name}/{name}.csproj", name, new RepositoryAssembly($"src/{name}/{name}.csproj", name, []))).ToArray();
        var edges = projects.Select(project => new DependencyEdge(new DependencyNode(DependencyKind.Assembly, project.Id, project.Name, project.Id),
            new DependencyNode(DependencyKind.Assembly, "external:Volo.Abp.Core", "Volo.Abp.Core, Version=10.7.1.0, Culture=neutral, PublicKeyToken=null"),
            [new DependencyEvidence(project.Id, null, new("references ABP"))])).ToArray();
        var observations = new DependencyInventoryResult(new RepositoryInventory("App.slnx", projects),
            new DependencyGraph(edges.SelectMany(edge => new[] { edge.Source, edge.Target }).DistinctBy(node => node.Id), edges), AnalysisStatus.Completed);
        var proposal = new PolicyProposalBuilder().Build(observations, TestContext.Current.CancellationToken);
        Assert.NotNull(proposal.Policy);
        Assert.DoesNotContain("ABP003", proposal.Policy.SeverityPolicy.RuleSeverities.Keys);
        Assert.Contains("rule:ABP003", proposal.Pack!.Exclusions);
        var yaml = ArchitectureGuardian.Configuration.Yaml.ProposedPolicyYaml.Serialize(proposal, TestContext.Current.CancellationToken);
        Assert.Contains("extends:", yaml, StringComparison.Ordinal);
        Assert.Contains("\"rule:ABP003\"", yaml, StringComparison.Ordinal);
    }
}
