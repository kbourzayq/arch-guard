using System.Text;
using System.Text.Json;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Discovery;
using ArchitectureGuardian.Core.Repositories;
using ArchitectureGuardian.Reporting;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Reporting;

/// <summary>Verifies wire compatibility, complete evidence and explicit coverage in discovery reports.</summary>
public sealed class DiscoveryReportTests
{
    /// <summary>Reports unavailable context without converting it into configuration validity or compliance.</summary>
    /// <param name="status">Noncompleted coverage.</param>
    /// <param name="expected">Version-one wire spelling.</param>
    [Theory]
    [InlineData(AnalysisStatus.Incomplete, "incomplete")]
    [InlineData(AnalysisStatus.Failed, "failed")]
    [InlineData(AnalysisStatus.Cancelled, "cancelled")]
    public async Task Noncompleted_reports_preserve_coverage_and_unresolved_intentions(AnalysisStatus status, string expected)
    {
        var observations = new DependencyInventoryResult(null, null, status,
            status == AnalysisStatus.Cancelled ? [] : [new("unavailable", "Resolve the missing context.")]);
        var proposal = new PolicyProposalBuilder().Build(observations, TestContext.Current.CancellationToken);
        var result = new DiscoveryResult("App.slnx", "Release", null, [], [], observations, proposal);
        var text = DiscoveryReportSerializer.Serialize(result);
        using var destination = new MemoryStream();
        await DiscoveryReportSerializer.SerializeAsync(destination, result, TestContext.Current.CancellationToken);
        Assert.Equal(Encoding.UTF8.GetBytes(text), destination.ToArray());
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

    /// <summary>Writes a large evidence array in chunks without changing its bytes or dropping any locations.</summary>
    [Fact]
    public async Task Large_report_streams_complete_evidence_in_bounded_writes()
    {
        const int locationCount = 4096;
        var result = ReportWithEvidence(locationCount);
        using var destination = new ChunkedDestination();
        await DiscoveryReportSerializer.SerializeAsync(destination, result, TestContext.Current.CancellationToken);

        Assert.True(destination.CanWrite);
        Assert.True(destination.WriteCount > 2);
        var bytes = destination.ToArray();
        Assert.True(bytes.Length >= 2 * 1024 * 1024);
        Assert.Equal(Encoding.UTF8.GetBytes(DiscoveryReportSerializer.Serialize(result)), bytes);
        using var report = JsonDocument.Parse(bytes);
        Assert.Equal("readyForReview", report.RootElement.GetProperty("proposalOutcome").GetString());
        Assert.Equal("passed", report.RootElement.GetProperty("compliance").GetProperty("gateOutcome").GetString());
        var evidence = report.RootElement.GetProperty("observations").GetProperty("graph")
            .GetProperty("edges")[0].GetProperty("evidence");
        Assert.Equal(Enumerable.Range(1, locationCount), evidence.EnumerateArray()
            .Select(item => item.GetProperty("fact").GetProperty("location").GetProperty("startLine").GetInt32()).Order());
    }

    /// <summary>Propagates cancellation from an in-progress write and leaves the stream open.</summary>
    [Fact]
    public async Task Cancellation_during_streaming_propagates()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var destination = new ChunkedDestination(count => { if (count == 2) { cancellation.Cancel(); } });

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DiscoveryReportSerializer.SerializeAsync(destination, ReportWithEvidence(256), cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(2, destination.WriteCount);
        Assert.True(destination.CanWrite);
    }

    /// <summary>Propagates an output error from an in-progress write and leaves the stream open.</summary>
    [Fact]
    public async Task Failure_during_streaming_propagates()
    {
        var failure = new IOException("Injected output failure.");
        using var destination = new ChunkedDestination(count => { if (count == 2) { throw failure; } });

        var error = await Assert.ThrowsAsync<IOException>(() =>
            DiscoveryReportSerializer.SerializeAsync(destination, ReportWithEvidence(256), TestContext.Current.CancellationToken));

        Assert.Same(failure, error);
        Assert.Equal(2, destination.WriteCount);
        Assert.True(destination.CanWrite);
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

    private static DiscoveryResult ReportWithEvidence(int locationCount)
    {
        const string application = "Shop.Application";
        const string domain = "Shop.Domain";
        const string applicationPath = "src/Shop.Application/Shop.Application.csproj";
        const string domainPath = "src/Shop.Domain/Shop.Domain.csproj";
        var inventory = new RepositoryInventory("Shop.slnx",
        [
            new(applicationPath, application, new(applicationPath, application, [])),
            new(domainPath, domain, new(domainPath, domain, [])),
        ]);
        var source = new DependencyNode(DependencyKind.Type, "Shop.Application.OrderService", "OrderService", applicationPath);
        var target = new DependencyNode(DependencyKind.Type, "Shop.Domain.Order", "Order", domainPath);
        var edge = new DependencyEdge(source, target, Enumerable.Range(1, locationCount).Select(line => new DependencyEvidence(
            applicationPath, domainPath, new("Uses the Order type when processing the application request.",
                new SourceLocation("src/Shop.Application/Services/OrderService.cs", line, 5, line, 10)),
            source.Identity, target.Identity, targetAssembly: domain, targetNamespace: domain)));
        var observations = new DependencyInventoryResult(inventory, new DependencyGraph([source, target], [edge]), AnalysisStatus.Completed);
        var proposal = new PolicyProposalBuilder().Build(observations, TestContext.Current.CancellationToken);
        return new("Shop.slnx", "Release", null, [applicationPath, domainPath], [], observations, proposal, new(AnalysisStatus.Completed));
    }

    /// <summary>Captures bytes from a non-seekable destination with a write-size limit.</summary>
    private sealed class ChunkedDestination(Action<int>? onWrite = null) : MemoryStream
    {
        public override bool CanSeek => false;
        public int WriteCount { get; private set; }

        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Assert.InRange(buffer.Length, 1, 128 * 1024);
            WriteCount++;
            onWrite?.Invoke(WriteCount);
            return base.WriteAsync(buffer, cancellationToken);
        }
    }
}
