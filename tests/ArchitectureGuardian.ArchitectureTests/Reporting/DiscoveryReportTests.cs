using System.Text.Json;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Discovery;
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
}
