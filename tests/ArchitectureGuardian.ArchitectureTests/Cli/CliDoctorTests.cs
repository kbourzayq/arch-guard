using System.Diagnostics;
using System.Text.Json;
using ArchitectureGuardian.Analysis.Doctor;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using ArchitectureGuardian.Cli;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Cli;

/// <summary>Exercises diagnostic output, trust, environmental blockers and effective compiler installation using isolated consumers.</summary>
[Trait("Category", "Integration")]
[Collection(nameof(CliWorkspaceTests))]
public sealed class CliDoctorTests
{
    /// <summary>Static diagnosis never runs build sentinels, restores assets or changes inputs; machine output is byte stable.</summary>
    [Fact]
    public async Task Static_checks_are_read_only_actionable_and_deterministic()
    {
        using var files = new DoctorFiles();
        var before = File.ReadAllText(files.Project);
        var first = await RunAsync(files);
        var second = await RunAsync(files);
        Assert.Equal(first, second);
        Assert.Equal(2, first.ExitCode);
        Assert.Empty(first.Error);
        using var report = JsonDocument.Parse(first.Output);
        Assert.Equal(1, report.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("notEvaluated", report.RootElement.GetProperty("compliance").GetString());
        Assert.Equal(JsonValueKind.Null, report.RootElement.GetProperty("workspaceStatus").ValueKind);
        Assert.Equal(0, report.RootElement.GetProperty("exploredContexts").GetArrayLength());
        Assert.Equal("notRun", Check(report, "workspace").GetProperty("status").GetString());
        Assert.Equal("warning", Check(report, "assets-hint").GetProperty("status").GetString());
        Assert.Equal("informational", Check(report, "mcp-integration").GetProperty("status").GetString());
        Assert.False(File.Exists(files.Marker));
        Assert.False(Directory.Exists(Path.Combine(files.Root, "App", "obj")));
        Assert.Equal(before, File.ReadAllText(files.Project));
        Assert.DoesNotContain('\r', first.Output);
        var text = await RunAsync(files, "--format", "text");
        Assert.Contains("Environment/configuration health does not imply architecture compliance", text.Output);
        Assert.Contains("[evaluated/notRun/required]", text.Output);
        Assert.Contains("Next:", text.Output);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var result = await new DoctorService().DiagnoseAsync(new(files.Input, true, "Release"), files.Policy, "scan", new YamlPolicyLoader(), cancelled.Token);
        Assert.Equal(AnalysisStatus.Cancelled, result.Status);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Contexts);
        Assert.False(File.Exists(files.Marker));
    }

    /// <summary>Malformed YAML and missing SDK selections are retained in the stable diagnostic envelope with remediation.</summary>
    /// <param name="scenario">The static blocker to introduce.</param>
    [Theory]
    [InlineData("yaml")]
    [InlineData("sdk")]
    [InlineData("input")]
    public async Task Static_blockers_have_machine_readable_remediation(string scenario)
    {
        using var files = new DoctorFiles();
        if (scenario == "yaml") { File.WriteAllText(files.Policy, "version: 99\n"); }
        if (scenario == "sdk") { File.WriteAllText(Path.Combine(files.Root, "global.json"), "{\"sdk\":{\"version\":\"99.0.100\",\"rollForward\":\"disable\"}}"); }
        if (scenario == "input") { File.Delete(files.Input); }
        var result = await RunAsync(files);
        Assert.Equal(2, result.ExitCode);
        using var report = JsonDocument.Parse(result.Output);
        var check = Check(report, scenario == "yaml" ? "policy" : scenario);
        Assert.Equal("blocked", check.GetProperty("status").GetString());
        Assert.NotEmpty(check.GetProperty("remediation").GetString()!);
        if (scenario == "yaml") { Assert.Contains("(1,", check.GetProperty("message").GetString()); }
        Assert.False(File.Exists(files.Marker));
    }

    /// <summary>Input paths obey the same policy-root contract as a separate scan before any build is evaluated.</summary>
    [Fact]
    public async Task A_solution_above_the_policy_root_is_a_static_blocker()
    {
        using var files = new DoctorFiles();
        var nestedPolicy = Path.Combine(files.Root, "App", "architecture.guardian.yaml");
        File.WriteAllText(nestedPolicy, File.ReadAllText(files.Policy).Replace("App/*.csproj", "*.csproj", StringComparison.Ordinal));
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await CliApplication.RunAsync(["doctor", files.Input, "--policy", nestedPolicy, "--trusted", "--configuration", "Release", "--format", "json"], output, error, TestContext.Current.CancellationToken);
        Assert.Equal(2, code);
        using var report = JsonDocument.Parse(output.ToString());
        Assert.Equal("blocked", Check(report, "input-root").GetProperty("status").GetString());
        Assert.Contains("../Input.slnx", Check(report, "input-root").GetProperty("message").GetString());
        Assert.Equal("notRun", Check(report, "workspace").GetProperty("status").GetString());
        Assert.False(File.Exists(files.Marker));
    }

    /// <summary>The public command reports a runtime mismatch even when its metadata host selects a valid SDK.</summary>
    [Fact]
    public async Task Runtime_mismatch_is_a_required_blocker_without_project_evaluation()
    {
        using var files = new DoctorFiles();
        var host = Path.Combine(files.Root, "MetadataHost");
        Directory.CreateDirectory(host);
        File.WriteAllText(Path.Combine(host, "MetadataHost.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(host, "Program.cs"), "System.Console.WriteLine(args[0] == \"--version\" ? \"10.0.402\" : \"Microsoft.NETCore.App 9.0.0 [test]\");");
        var build = await Repository.Dotnet("build", Path.Combine(host, "MetadataHost.csproj"), "--source", files.Root, "--disable-build-servers");
        Assert.True(build.ExitCode == 0, build.Output + build.Error);
        var executable = Path.Combine(host, "bin", "Debug", "net10.0", "MetadataHost" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var result = await RunProcessAsync(files, executable);
        Assert.Equal(2, result.ExitCode);
        using var report = JsonDocument.Parse(result.Output);
        Assert.Equal("passed", Check(report, "sdk").GetProperty("status").GetString());
        Assert.Equal("blocked", Check(report, "runtime").GetProperty("status").GetString());
        Assert.Contains(".NET 10", Check(report, "runtime").GetProperty("remediation").GetString());
        Assert.False(File.Exists(files.Marker));
    }

    /// <summary>Trusted diagnostics reuse loading coverage and the strict ownership resolver, including stale and ambiguous selectors.</summary>
    [Fact]
    public async Task Trusted_checks_preserve_context_partial_coverage_and_ownership_errors()
    {
        using var files = new DoctorFiles();
        await files.RestoreAsync();
        File.Delete(files.Marker);
        var healthy = await RunAsync(files, "--trusted", "--configuration", "Release", "--framework", "net10.0");
        Assert.True(healthy.ExitCode == 0, healthy.Output + healthy.Error);
        Assert.True(File.Exists(files.Marker));
        using var complete = JsonDocument.Parse(healthy.Output);
        Assert.Equal("completed", complete.RootElement.GetProperty("workspaceStatus").GetString());
        Assert.Equal("Release", complete.RootElement.GetProperty("exploredContexts")[0].GetProperty("configuration").GetString());
        Assert.Equal("net10.0", complete.RootElement.GetProperty("exploredContexts")[0].GetProperty("framework").GetString());
        Assert.Equal("notEvaluated", complete.RootElement.GetProperty("compliance").GetString());
        var original = File.ReadAllText(files.Policy);
        foreach (var (yaml, expected) in new[]
        {
            (original.Replace("projects: [App/*.csproj]", "projects: [Missing/*.csproj]", StringComparison.Ordinal), "matches no project"),
            (original.Replace("layers:", "  - id: Other\n    projects: [App/*.csproj]\nlayers:", StringComparison.Ordinal), "exactly one module"),
            (original.Replace("projects: [App/*.csproj]", "projects: [App/*.csproj, Stale/*.csproj]", StringComparison.Ordinal), "matches no project"),
        })
        {
            File.WriteAllText(files.Policy, yaml);
            var invalid = await RunAsync(files, "--trusted", "--configuration", "Release");
            Assert.Equal(2, invalid.ExitCode);
            using var report = JsonDocument.Parse(invalid.Output);
            Assert.Equal("blocked", Check(report, "ownership").GetProperty("status").GetString());
            Assert.Contains(expected, Check(report, "ownership").GetProperty("message").GetString());
        }
        File.WriteAllText(files.Policy, original);
        var other = Path.Combine(files.Root, "Other");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "Other.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        var solution = File.ReadAllText(files.Input);
        File.WriteAllText(files.Input, solution.Replace("</Solution>", "<Project Path=\"Other/Other.csproj\" /></Solution>", StringComparison.Ordinal));
        File.WriteAllText(files.Policy, original.Replace("  - id: All\n    projects: [App/*.csproj]", "  - id: All\n    projects: ['**/*.csproj']", StringComparison.Ordinal));
        await files.RestoreAsync();
        var unmapped = await RunAsync(files, "--trusted", "--configuration", "Release");
        Assert.Equal(2, unmapped.ExitCode);
        using var unmappedReport = JsonDocument.Parse(unmapped.Output);
        Assert.Contains("matched modules []", Check(unmappedReport, "ownership").GetProperty("message").GetString());
        File.WriteAllText(files.Input, solution);
        File.WriteAllText(files.Policy, original);
        File.WriteAllText(files.Project, File.ReadAllText(files.Project).Replace("<TargetFramework>net10.0</TargetFramework>", "<TargetFrameworks>net10.0;netstandard2.1</TargetFrameworks>", StringComparison.Ordinal));
        await files.RestoreAsync();
        var unspecified = await RunAsync(files, "--trusted", "--configuration", "Release");
        Assert.Equal(2, unspecified.ExitCode);
        Assert.Contains("framework-required", unspecified.Output);
        var selected = await RunAsync(files, "--trusted", "--configuration", "Release", "--framework", "netstandard2.1");
        Assert.True(selected.ExitCode == 0, selected.Output + selected.Error);
        using var selectedReport = JsonDocument.Parse(selected.Output);
        Assert.Equal("netstandard2.1", selectedReport.RootElement.GetProperty("exploredContexts")[0].GetProperty("framework").GetString());
        File.Delete(Path.Combine(files.Root, "App", "obj", "project.assets.json"));
        var missingAssets = await RunAsync(files, "--trusted", "--configuration", "Release", "--framework", "net10.0");
        Assert.Equal(2, missingAssets.ExitCode);
        using var partial = JsonDocument.Parse(missingAssets.Output);
        Assert.Equal("incomplete", partial.RootElement.GetProperty("workspaceStatus").GetString());
        Assert.Contains("restore", missingAssets.Output, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(files.Root, "App", "obj", "project.assets.json")));
    }

    /// <summary>Real packaged CPM/non-CPM consumers expose root installation, AdditionalFiles, defaults, overrides and inactive rules.</summary>
    /// <param name="central">Whether the consumer uses central package management.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Compiler_workflow_checks_packaged_installation_and_effective_severities(bool central)
    {
        using var files = new DoctorFiles();
        var feed = Path.Combine(files.Root, "feed");
        Directory.CreateDirectory(feed);
        var packed = await Repository.Dotnet("pack", Path.Combine(Repository.Root, "src", "ArchitectureGuardian.Analyzers"), "--configuration", "Release", "--no-build", "--no-restore", "--output", feed);
        Assert.True(packed.ExitCode == 0, packed.Output + packed.Error);
        var packageVersion = typeof(CliApplication).Assembly.GetName().Version!.ToString(3);
        File.WriteAllText(Path.Combine(files.Root, "Directory.Build.props"), "<Project><ItemGroup><PackageReference Include=\"ArchitectureGuardian.Analyzers\" PrivateAssets=\"all\""
            + (central ? "" : " Version=\"" + packageVersion + "\"") + "/><AdditionalFiles Include=\"$(MSBuildThisFileDirectory)architecture.guardian.yaml\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(files.Root, "Directory.Packages.props"), central
            ? "<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup><ItemGroup><PackageVersion Include=\"ArchitectureGuardian.Analyzers\" Version=\"" + packageVersion + "\" /></ItemGroup></Project>"
            : "<Project />");
        File.WriteAllText(Path.Combine(files.Root, "App", ".editorconfig"), "root = true\n[*.cs]\ndotnet_diagnostic.AG001.severity = none\ndotnet_diagnostic.AG004.severity = error\n");
        File.WriteAllText(files.Policy, File.ReadAllText(files.Policy).Replace("AG001: error", "AG001: error\n  AG002: warning", StringComparison.Ordinal));
        await files.RestoreAsync(feed);
        var result = await RunAsync(files, "--trusted", "--configuration", "Release", "--workflow", "compiler");
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        using var report = JsonDocument.Parse(result.Output);
        Assert.Equal("passed", Check(report, "analyzer-installation").GetProperty("status").GetString());
        Assert.Equal("passed", Check(report, "analyzer-policy").GetProperty("status").GetString());
        Assert.Equal(result, await RunAsync(files, "--trusted", "--configuration", "Release", "--workflow", "compiler"));
        Assert.Contains("effective compiler setting=suppress", result.Output);
        Assert.Contains("effective compiler setting=warning", result.Output);
        Assert.Contains("inactive (omitted from YAML)", result.Output);
        Assert.Contains(Path.Combine(files.Root, "Directory.Build.props").Replace("\\", "\\\\", StringComparison.Ordinal), result.Output);
        var props = Path.Combine(files.Root, "Directory.Build.props");
        var originalProps = File.ReadAllText(props);
        var targets = Path.Combine(files.Root, "Directory.Build.targets");
        File.WriteAllText(targets, "<Project><Target Name=\"RemoveDoctorPolicy\" BeforeTargets=\"CoreCompile\"><ItemGroup><AdditionalFiles Remove=\"$(MSBuildThisFileDirectory)architecture.guardian.yaml\" /></ItemGroup></Target></Project>");
        var removed = await RunAsync(files, "--trusted", "--configuration", "Release", "--workflow", "compiler");
        Assert.Equal(2, removed.ExitCode);
        using var removedReport = JsonDocument.Parse(removed.Output);
        Assert.Equal("blocked", Check(removedReport, "analyzer-policy").GetProperty("status").GetString());
        File.WriteAllText(props, originalProps.Replace("<AdditionalFiles Include=\"$(MSBuildThisFileDirectory)architecture.guardian.yaml\" />", "", StringComparison.Ordinal));
        File.WriteAllText(targets, "<Project><Target Name=\"AddDoctorPolicy\" BeforeTargets=\"CoreCompile\"><ItemGroup><AdditionalFiles Include=\"$(MSBuildThisFileDirectory)architecture.guardian.yaml\" /></ItemGroup></Target></Project>");
        var added = await RunAsync(files, "--trusted", "--configuration", "Release", "--workflow", "compiler");
        Assert.True(added.ExitCode == 0, added.Output + added.Error);
        using var addedReport = JsonDocument.Parse(added.Output);
        Assert.Equal("passed", Check(addedReport, "analyzer-policy").GetProperty("status").GetString());
        File.Delete(targets);
        File.WriteAllText(props, originalProps.Replace("architecture.guardian.yaml", "architecture.guardian.compiler.xml", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(files.Root, "architecture.guardian.compiler.xml"), "<ArchitectureGuardian version=\"1\"><Module id=\"App\" /><Layer id=\"All\" /><Assembly name=\"App\" module=\"App\" layer=\"All\" /></ArchitectureGuardian>");
        var legacy = await RunAsync(files, "--trusted", "--configuration", "Release", "--workflow", "compiler");
        Assert.True(legacy.ExitCode == 0, legacy.Output + legacy.Error);
        using var legacyReport = JsonDocument.Parse(legacy.Output);
        Assert.Equal("passed", Check(legacyReport, "analyzer-policy").GetProperty("status").GetString());
        Assert.Equal("notRun", Check(legacyReport, "legacy-policy-validation").GetProperty("status").GetString());
        Assert.False(Check(legacyReport, "legacy-policy-validation").GetProperty("required").GetBoolean());
        Assert.Contains("YAML=not applied (legacy XML)", legacy.Output);
        Assert.Contains("AG006: selected=True; YAML=not applied (legacy XML); editorconfig override=default; effective compiler setting=warning", legacy.Output);
        var built = await Repository.Dotnet("build", files.Input, "--configuration", "Release", "--no-restore", "--disable-build-servers");
        Assert.True(built.ExitCode == 0, built.Output + built.Error);
        foreach (var property in new[] { "RunAnalyzers", "RunAnalyzersDuringBuild" })
        {
            File.WriteAllText(props, originalProps.Replace("</Project>", $"<PropertyGroup><{property}>false</{property}></PropertyGroup></Project>", StringComparison.Ordinal));
            var disabled = await RunAsync(files, "--trusted", "--configuration", "Release", "--workflow", "compiler");
            Assert.Equal(2, disabled.ExitCode);
            using var disabledReport = JsonDocument.Parse(disabled.Output);
            Assert.Equal("blocked", Check(disabledReport, "analyzer-execution").GetProperty("status").GetString());
            var optional = await RunAsync(files, "--trusted", "--configuration", "Release");
            Assert.True(optional.ExitCode == 0, optional.Output + optional.Error);
            using var optionalReport = JsonDocument.Parse(optional.Output);
            Assert.Equal("informational", Check(optionalReport, "analyzer-execution").GetProperty("status").GetString());
        }
        File.WriteAllText(props, originalProps);
        File.WriteAllText(props, File.ReadAllText(props).Replace("<AdditionalFiles Include=", "<AdditionalFiles Condition=\"'$(Configuration)' == 'Debug'\" Include=", StringComparison.Ordinal));
        var broken = await RunAsync(files, "--trusted", "--configuration", "Release", "--workflow", "compiler");
        Assert.Equal(2, broken.ExitCode);
        using var brokenReport = JsonDocument.Parse(broken.Output);
        Assert.Equal("blocked", Check(brokenReport, "analyzer-policy").GetProperty("status").GetString());
    }

    private static JsonElement Check(JsonDocument document, string id) => document.RootElement.GetProperty("checks").EnumerateArray().First(check => check.GetProperty("id").GetString() == id);

    private static async Task<CommandResult> RunAsync(DoctorFiles files, params string[] options)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var format = options.Contains("--format", StringComparer.Ordinal) ? Array.Empty<string>() : ["--format", "json"];
        var code = await CliApplication.RunAsync(["doctor", files.Input, "--policy", files.Policy, .. format, .. options], output, error, TestContext.Current.CancellationToken);
        return new(code, output.ToString(), error.ToString());
    }

    private static async Task<CommandResult> RunProcessAsync(DoctorFiles files, string host)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = files.Root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { typeof(CliApplication).Assembly.Location, "doctor", files.Input, "--policy", files.Policy, "--format", "json" }) { start.ArgumentList.Add(argument); }
        start.Environment["DOTNET_HOST_PATH"] = host;
        using var process = Process.Start(start)!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);
        try { await process.WaitForExitAsync(deadline.Token); return new(process.ExitCode, await output, await error); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
    }

    /// <summary>Owns a package-free consumer whose build sentinel detects implicit evaluation or restore.</summary>
    private sealed class DoctorFiles : IDisposable
    {
        internal DoctorFiles()
        {
            Root = Path.Combine(Path.GetTempPath(), "archguard-doctor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "App"));
            File.Copy(Path.Combine(Repository.Root, "global.json"), Path.Combine(Root, "global.json"));
            File.WriteAllText(Path.Combine(Root, "Directory.Build.props"), "<Project />");
            File.WriteAllText(Path.Combine(Root, "Directory.Packages.props"), "<Project />");
            File.WriteAllText(Input, "<Solution><Project Path=\"App/App.csproj\" /></Solution>");
            File.WriteAllText(Project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>"
                + "<Target Name=\"DoctorSentinel\" BeforeTargets=\"CoreCompile\"><WriteLinesToFile File=\"$(MSBuildProjectDirectory)/../evaluated.txt\" Lines=\"evaluated\" Overwrite=\"true\" /></Target></Project>");
            File.WriteAllText(Path.Combine(Root, "App", "Class.cs"), "public class Consumer { }");
            File.WriteAllText(Policy, "version: 1\nmodules:\n  - id: App\n    projects: [App/*.csproj]\nlayers:\n  - id: All\n    projects: [App/*.csproj]\nrules:\n  AG001: error\n");
        }

        internal string Root { get; }
        internal string Project => Path.Combine(Root, "App", "App.csproj");
        internal string Input => Path.Combine(Root, "Input.slnx");
        internal string Policy => Path.Combine(Root, "architecture.guardian.yaml");
        internal string Marker => Path.Combine(Root, "evaluated.txt");

        internal async Task RestoreAsync(string? source = null)
        {
            var result = await Repository.Dotnet("restore", Input, "--source", source ?? Root, "-p:Configuration=Release", "-p:NuGetAudit=false", "--disable-build-servers");
            Assert.True(result.ExitCode == 0, result.Output + result.Error);
        }

        /// <summary>Removes all isolated consumer inputs and outputs.</summary>
        public void Dispose() => Directory.Delete(Root, true);
    }
}
