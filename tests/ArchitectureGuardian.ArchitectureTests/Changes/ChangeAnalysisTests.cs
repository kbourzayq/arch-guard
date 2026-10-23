using System.IO.Compression;
using System.Text.Json;
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

/// <summary>Verifies full-context Git comparisons and agreement with independent full-scan fixtures.</summary>
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

    /// <summary>Each tree uses its own inventory and policy; renamed identities remain conservative new findings.</summary>
    /// <param name="scenario">The candidate-only policy or project inventory change.</param>
    [Theory]
    [InlineData("policy")]
    [InlineData("rename")]
    [InlineData("remove")]
    public async Task Policy_and_project_changes_match_full_scan_comparison(string scenario)
    {
        using var fixture = new ChangeInputFiles();
        fixture.AddReference();
        await fixture.Initialize();
        var baseline = await fixture.FullScan();
        Assert.NotEmpty(baseline.Diagnostics);
        if (scenario == "policy")
        {
            fixture.Write("policy.yaml", ChangeInputFiles.Policy.Replace("- id: App", "- id: App\n    allowedDependencies: [\"module:Library\"]", StringComparison.Ordinal));
        }
        else if (scenario == "rename")
        {
            Directory.Move(Path.Combine(fixture.Root, "Library"), Path.Combine(fixture.Root, "Renamed"));
            foreach (var path in new[] { "Input.slnx", "policy.yaml", "Directory.Build.targets" })
            {
                var content = File.ReadAllText(Path.Combine(fixture.Root, path)).Replace("Library/", "Renamed/", StringComparison.Ordinal);
                fixture.Write(path, content);
            }
        }
        else
        {
            fixture.Write("Input.slnx", "<Solution><Project Path=\"Library/Library.csproj\" /></Solution>");
            fixture.Write("policy.yaml", ChangeInputFiles.Policy.Replace("  - id: App\n    projects: [\"App/*.csproj\"]\n", "", StringComparison.Ordinal));
        }

        var candidate = await fixture.FullScan();
        var expected = new BaselineComparison(candidate, BaselineSnapshot.Capture(baseline));
        var changes = await fixture.Changes();
        Assert.True(changes.Analysis.IsComplete, string.Join('\n', changes.Analysis.Issues.Select(item => item.Message)));
        Assert.Equal(candidate.Diagnostics.Select(item => item.Id), changes.Analysis.Diagnostics.Select(item => item.Id));
        Assert.Equal(expected.Existing.Select(item => item.Id), changes.Comparison!.Existing.Select(item => item.Id));
        Assert.Equal(expected.New.Select(item => item.Id), changes.Comparison.New.Select(item => item.Id));
        Assert.Equal(expected.Resolved.Select(item => item.Fingerprint), changes.Comparison.Resolved.Select(item => item.Fingerprint));
        Assert.Equal(scenario != "rename", changes.GatePassed);
        Assert.NotEmpty(changes.Comparison.Resolved);
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

    /// <summary>Missing refs and shallow history, absent snapshot paths, invalid base policy and unavailable packages cannot pass.</summary>
    /// <param name="scenario">The unavailable base or snapshot input.</param>
    [Theory]
    [InlineData("ref")]
    [InlineData("policy")]
    [InlineData("input")]
    [InlineData("packages")]
    [InlineData("candidate-policy")]
    [InlineData("shallow")]
    public async Task Unavailable_snapshots_do_not_return_success(string scenario)
    {
        using var fixture = new ChangeInputFiles();
        if (scenario == "policy") { fixture.Write("policy.yaml", "version: 99"); }
        if (scenario == "packages") { fixture.Write("Directory.Build.targets", "<Project><ItemGroup><PackageReference Include=\"Archguard.Tests.DoesNotExist\" Version=\"1.0.0\" /></ItemGroup></Project>"); }
        await fixture.Initialize();
        if (scenario == "policy") { fixture.Write("policy.yaml", ChangeInputFiles.Policy); }
        if (scenario == "candidate-policy") { fixture.Write("policy.yaml", "version: 99"); }
        if (scenario == "packages") { fixture.Write("Directory.Build.targets", "<Project />"); }
        if (scenario == "input") { fixture.Write("Other.slnx", File.ReadAllText(fixture.Input)); }
        var request = new SolutionAnalysisService();
        var input = scenario == "input" ? Path.Combine(fixture.Root, "Other.slnx") : fixture.Input;
        var reference = scenario is "ref" or "shallow" ? "HEAD~1" : "HEAD";
        var root = fixture.Root;
        var clone = Path.Combine(fixture.Root, "shallow");
        if (scenario == "shallow")
        {
            fixture.Write("App/Existing.cs", "public class Changed { }");
            await fixture.Git("add", ".");
            await fixture.Git("-c", "user.name=Architecture Tests", "-c", "user.email=tests@example.invalid", "commit", "-m", "next");
            await fixture.Git("clone", "--depth=1", new Uri(fixture.Root + Path.DirectorySeparatorChar).AbsoluteUri, clone);
            root = clone;
            input = Path.Combine(root, "Input.slnx");
            Assert.True(File.Exists(Path.Combine(clone, ".git", "shallow")));
        }

        var changes = await request.AnalyzeChangesAsync(new(input, true), Path.Combine(root, "policy.yaml"), reference,
            new YamlPolicyLoader(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(changes.Analysis.IsComplete);
        Assert.False(changes.GatePassed);
        if (scenario == "candidate-policy") { Assert.Empty(changes.Comparison!.Resolved); }
        else { Assert.Null(changes.Comparison); }
        Assert.NotEmpty(changes.Analysis.Issues);
        if (scenario == "ref")
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(2, await CliApplication.RunAsync(["scan", input, "--policy", fixture.PolicyPath, "--base", reference, "--trusted"],
                output, error, TestContext.Current.CancellationToken));
            Assert.DoesNotContain("Gate passed", output.ToString());
            Assert.Contains("change-snapshot-unavailable", error.ToString());
        }
    }

    /// <summary>Trust and cancellation are checked before Git/build work and operator boundaries cannot be widened.</summary>
    [Fact]
    public async Task Trust_cancellation_and_operator_root_are_enforced()
    {
        using var fixture = new ChangeInputFiles();
        var service = new SolutionAnalysisService();
        var untrusted = await service.AnalyzeChangesAsync(new(fixture.Input), fixture.PolicyPath, "HEAD", new YamlPolicyLoader(),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(untrusted.Analysis.Issues, item => item.Code == "workspace-untrusted");
        Assert.False(untrusted.GatePassed);
        var cancelled = await fixture.Changes(token: new CancellationToken(true));
        Assert.Equal(AnalysisStatus.Cancelled, cancelled.Analysis.Status);
        await fixture.Initialize();
        var outside = await service.AnalyzeChangesAsync(new(fixture.Input, true), fixture.PolicyPath, "HEAD", new YamlPolicyLoader(),
            Path.Combine(fixture.Root, "App"), TestContext.Current.CancellationToken);
        Assert.False(outside.GatePassed);
        Assert.Contains(outside.Analysis.Issues, item => item.Code == "change-snapshot-unavailable");
    }

    /// <summary>Project-defined feeds cannot extend the temporary snapshot's cache-only restore sources.</summary>
    [Fact]
    public async Task Additional_project_sources_are_not_searched()
    {
        using var fixture = new ChangeInputFiles();
        var feed = Directory.CreateDirectory(Path.Combine(fixture.Root, "AdditionalFeed")).FullName;
        var packageId = "Archguard.Review." + Guid.NewGuid().ToString("N");
        using (var package = ZipFile.Open(Path.Combine(feed, packageId + ".1.0.0.nupkg"), ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(package.CreateEntry(packageId + ".nuspec").Open()))
            {
                writer.Write($"<package><metadata><id>{packageId}</id><version>1.0.0</version><authors>Tests</authors><description>Source isolation fixture.</description></metadata></package>");
            }
            package.CreateEntry("lib/net10.0/_._");
        }
        fixture.Write("Directory.Build.targets", $$"""
            <Project>
              <PropertyGroup>
                <RestoreAdditionalProjectSources>{{System.Security.SecurityElement.Escape(feed)}}</RestoreAdditionalProjectSources>
                <RestorePackagesPath>$(MSBuildThisFileDirectory).packages</RestorePackagesPath>
              </PropertyGroup>
              <ItemGroup><PackageReference Include="{{packageId}}" Version="1.0.0" /></ItemGroup>
            </Project>
            """);
        fixture.Write(".gitignore", "**/bin/\n**/obj/\n.packages/\n");
        // This package really is restorable from the additional source, into a fixture-owned cache.
        await fixture.FullScan();
        await fixture.Initialize();
        var changes = await fixture.Changes();
        Assert.False(changes.GatePassed);
        Assert.Contains(changes.Analysis.Issues, item => item.Code == "change-snapshot-load-failed");
        Assert.Contains(changes.Analysis.Issues, item => item.Message.Contains(packageId, StringComparison.Ordinal));
    }

    /// <summary>Configuration separators stay literal rather than injecting restore-only properties.</summary>
    /// <param name="value">A build-context value containing MSBuild property syntax.</param>
    /// <param name="framework">Whether to test the framework rather than configuration.</param>
    [Theory]
    [InlineData("Debug,Injected=true", false)]
    [InlineData("Debug;Injected=true", false)]
    [InlineData("Debug%3BInjected=true", false)]
    [InlineData("$(Context)", false)]
    [InlineData("net10.0;Injected=true", true)]
    public async Task Build_context_cannot_inject_restore_properties(string value, bool framework)
    {
        using var fixture = new ChangeInputFiles();
        if (!framework)
        {
            fixture.Write("Input.slnx", $$"""
                <Solution><Configurations><BuildType Name="{{System.Security.SecurityElement.Escape(value)}}" /></Configurations>
                  <Project Path="App/App.csproj" /><Project Path="Library/Library.csproj" />
                </Solution>
                """);
        }
        fixture.Write("Directory.Build.targets", """
            <Project><ItemGroup Condition="'$(Injected)' == 'true'">
              <PackageReference Include="Archguard.Tests.DoesNotExist" Version="1.0.0" />
            </ItemGroup></Project>
            """);
        await fixture.Initialize();
        var changes = await new SolutionAnalysisService().AnalyzeChangesAsync(new(fixture.Input, true, framework ? "Debug" : value, framework ? value : null),
            fixture.PolicyPath, "HEAD", new YamlPolicyLoader(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(changes.Analysis.IsComplete);
        Assert.False(changes.GatePassed);
        Assert.Equal("change-build-context-invalid", Assert.Single(changes.Analysis.Issues).Code);
        Assert.DoesNotContain("Archguard.Tests.DoesNotExist", string.Join('\n', changes.Analysis.Issues.Select(item => item.Message)));
    }

    /// <summary>Candidate-only attributes cannot smudge or corrupt source files in the base snapshot.</summary>
    [Fact]
    public async Task Base_checkout_uses_base_attributes()
    {
        using var fixture = new ChangeInputFiles();
        await fixture.Initialize();
        await fixture.Git("config", "filter.archguardreview.smudge", "git hash-object --stdin");
        fixture.Write(".gitattributes", "*.cs filter=archguardreview\n");
        var changes = await fixture.Changes();
        Assert.True(changes.Analysis.IsComplete, string.Join('\n', changes.Analysis.Issues.Select(item => item.Message)));
        Assert.True(changes.GatePassed);
    }
}
