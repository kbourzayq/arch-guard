using System.IO.Compression;
using System.Text.Json;
using ArchitectureGuardian.Analysis.Changes;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using ArchitectureGuardian.ArchitectureTests.Mcp;
using ArchitectureGuardian.Cli;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Baselines;
using ModelContextProtocol.Protocol;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Changes;

/// <summary>Verifies full-context Git comparisons of dirty trees and pre-existing debt against independent full scans.</summary>
[Trait("Category", "Integration")]
[Collection(nameof(ChangeAnalysisTests))]
public sealed class ChangeAnalysisTests
{
    /// <summary>An imported reference adds an edge between unchanged project files; all dirty-tree source states participate.</summary>
    [Fact]
    public async Task Dirty_tree_matches_full_scans_and_both_hosts_without_mutating_inputs()
    {
        using var fixture = new ChangeInputFiles();
        await fixture.Initialize();
        var baseline = await fixture.FullScan();
        var projects = new[] { "App/App.csproj", "Library/Library.csproj" }.ToDictionary(path => path,
            path => File.ReadAllBytes(Path.Combine(fixture.Root, path)));
        fixture.AddReference();
        await fixture.Git("add", "Directory.Build.targets");
        fixture.Write("App/Existing.cs", "public class Existing : Library.Target { }");
        fixture.Write("App/Untracked.cs", "public class Untracked : Library.Target { }");
        var current = await fixture.FullScan();
        var expected = new BaselineComparison(current, BaselineSnapshot.Capture(baseline));
        var status = await fixture.Git("status", "--porcelain=v1", "-z");
        var index = File.ReadAllBytes(Path.Combine(fixture.Root, ".git", "index"));
        var changes = await fixture.Changes();
        Assert.True(changes.Analysis.IsComplete, string.Join('\n', changes.Analysis.Issues.Select(item => item.Message)));
        Assert.False(changes.GatePassed);
        Assert.Equal((await fixture.Git("rev-parse", "HEAD")).Trim(), changes.BaseCommit);
        Assert.Equal(current.Diagnostics.Select(item => item.Id), changes.Analysis.Diagnostics.Select(item => item.Id));
        Assert.Equal(expected.New.Select(item => item.Id), changes.Comparison!.New.Select(item => item.Id));
        Assert.Empty(changes.Comparison.Existing);
        Assert.Contains(changes.Analysis.Diagnostics.SelectMany(item => item.Evidence), item => item.Location?.Path == "App/Untracked.cs");
        Assert.Contains(changes.Analysis.Diagnostics.SelectMany(item => item.Evidence), item => item.Location?.Path == "App/Existing.cs");

        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, await CliApplication.RunAsync(["scan", fixture.Input, "--policy", fixture.PolicyPath, "--base", "HEAD", "--trusted"],
            output, error, TestContext.Current.CancellationToken));
        Assert.Contains($"0 existing, {expected.New.Count} new, 0 resolved", output.ToString());
        Assert.Empty(error.ToString());
        await ToolRegistrationTests.WithServer(async (client, token) =>
        {
            var result = await client.CallToolAsync("analyze_changes", new Dictionary<string, object?>
            {
                ["inputPath"] = "Input.slnx",
                ["policyPath"] = "policy.yaml",
                ["baseReference"] = "HEAD",
                ["scope"] = "solution",
            }, cancellationToken: token);
            Assert.False(result.IsError, Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
            using var json = JsonDocument.Parse(result.StructuredContent!.Value.GetRawText());
            Assert.Equal("completed", json.RootElement.GetProperty("status").GetString());
            Assert.Equal("failed", json.RootElement.GetProperty("gateOutcome").GetString());
            Assert.Equal(changes.BaseCommit, json.RootElement.GetProperty("comparison").GetProperty("baseCommit").GetString());
            Assert.Equal(expected.New.Select(item => item.Id), json.RootElement.GetProperty("comparison").GetProperty("new").EnumerateArray().Select(item => item.GetString()));
        }, root: fixture.Root, trusted: true);
        Assert.Equal(status, await fixture.Git("status", "--porcelain=v1", "-z"));
        Assert.Equal(index, File.ReadAllBytes(Path.Combine(fixture.Root, ".git", "index")));
        foreach (var (path, bytes) in projects) { Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(fixture.Root, path))); }
    }

    /// <summary>Unchanged pre-existing debt is reported but does not fail a completed new-finding gate.</summary>
    [Fact]
    public async Task Existing_findings_pass_and_base_or_candidate_coverage_failures_block()
    {
        using var fixture = new ChangeInputFiles();
        fixture.AddReference();
        await fixture.Initialize();
        var unchanged = await fixture.Changes();
        Assert.True(unchanged.GatePassed, string.Join('\n', unchanged.Analysis.Issues.Select(item => item.Message)));
        Assert.NotEmpty(unchanged.Comparison!.Existing);
        Assert.Empty(unchanged.Comparison.New);
        fixture.Write("App/Broken.cs", "public class Broken : MissingType { }");
        var brokenCandidate = await fixture.Changes();
        Assert.False(brokenCandidate.GatePassed);
        Assert.Equal(AnalysisStatus.Incomplete, brokenCandidate.Analysis.Status);
        Assert.NotEmpty(brokenCandidate.Analysis.Diagnostics);
        Assert.Empty(brokenCandidate.Comparison!.Resolved);
        Assert.Contains(brokenCandidate.Analysis.Issues, item => item.Message.StartsWith("Candidate snapshot:", StringComparison.Ordinal));
        await fixture.Git("add", ".");
        await fixture.Git("-c", "user.name=Architecture Tests", "-c", "user.email=tests@example.invalid", "commit", "-m", "broken base");
        File.Delete(Path.Combine(fixture.Root, "App", "Broken.cs"));
        var brokenBase = await fixture.Changes();
        Assert.False(brokenBase.GatePassed);
        Assert.Null(brokenBase.Comparison);
        Assert.Contains(brokenBase.Analysis.Issues, item => item.Message.StartsWith("Base snapshot:", StringComparison.Ordinal));
    }
}
