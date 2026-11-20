using System.IO.Compression;
using ArchitectureGuardian.Analysis.Changes;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using ArchitectureGuardian.Cli;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Changes;

/// <summary>Verifies that unavailable, untrusted or tampered change snapshots never produce a passing comparison.</summary>
[Trait("Category", "Integration")]
[Collection(nameof(ChangeSnapshotSafetyTests))]
public sealed class ChangeSnapshotSafetyTests
{
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

    /// <summary>Snapshot copies live under an owner-only root so a private checkout is not exposed through a shared temporary directory.</summary>
    [Fact]
    public async Task Snapshot_root_is_owner_only_and_removed()
    {
        using var fixture = new ChangeInputFiles();
        await fixture.Initialize();
        string operationRoot;
        using (var snapshots = new GitSnapshots())
        {
            await snapshots.CaptureAsync(fixture.Root, "HEAD", TestContext.Current.CancellationToken);
            operationRoot = Path.GetDirectoryName(snapshots.BaseDirectory)!;
            Assert.Equal(operationRoot, Path.GetDirectoryName(snapshots.CandidateDirectory));
            Assert.True(File.Exists(Path.Combine(snapshots.CandidateDirectory, "App", "Existing.cs")));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(operationRoot));
            }
        }

        Assert.False(Directory.Exists(operationRoot));
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
