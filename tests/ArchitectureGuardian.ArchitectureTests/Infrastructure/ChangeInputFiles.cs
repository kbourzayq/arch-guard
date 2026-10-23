using System.Diagnostics;
using ArchitectureGuardian.Analysis.Changes;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Infrastructure;

/// <summary>Provides a real Git repository with package-free architecture inputs and explicit snapshot helpers.</summary>
internal sealed class ChangeInputFiles : IDisposable
{
    internal const string Policy = """
        version: 1
        modules:
          - id: App
            projects: ["App/*.csproj"]
          - id: Library
            projects: ["Library/*.csproj"]
        layers:
          - id: All
            projects: ["**/*.csproj"]
        rules:
          AG001: error
        """;

    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "archguard-git-" + Guid.NewGuid().ToString("N"));
    internal string Input => Path.Combine(Root, "Input.slnx");
    internal string PolicyPath => Path.Combine(Root, "policy.yaml");

    internal ChangeInputFiles()
    {
        Directory.CreateDirectory(Root);
        File.Copy(Path.Combine(Repository.Root, "global.json"), Path.Combine(Root, "global.json"));
        Write(".gitignore", "**/bin/\n**/obj/\n");
        Write(".gitattributes", "policy.yaml export-ignore\n");
        Write("Directory.Build.props", "<Project><PropertyGroup><RestorePackagesWithLockFile>false</RestorePackagesWithLockFile></PropertyGroup></Project>");
        Write("Directory.Build.targets", "<Project />");
        foreach (var name in new[] { "App", "Library" })
        {
            Write(name + "/" + name + ".csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        }

        Write("App/Existing.cs", "public class Existing { }");
        Write("Library/Target.cs", "namespace Library; public class Target { }");
        Write("Input.slnx", "<Solution><Project Path=\"App/App.csproj\" /><Project Path=\"Library/Library.csproj\" /></Solution>");
        Write("policy.yaml", Policy);
    }

    internal void Write(string path, string content)
    {
        var full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    internal void AddReference() => Write("Directory.Build.targets", """
        <Project><ItemGroup Condition="'$(MSBuildProjectName)' == 'App'">
          <ProjectReference Include="../Library/Library.csproj" />
        </ItemGroup></Project>
        """);

    internal async Task Initialize()
    {
        await Git("init", "-b", "main");
        await Git("add", ".");
        await Git("-c", "user.name=Architecture Tests", "-c", "user.email=tests@example.invalid", "commit", "-m", "fixture base");
    }

    internal async Task<string> Git(params string[] arguments) => await GitAt(Root, arguments);

    internal static async Task<string> GitAt(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        foreach (var argument in arguments) { start.ArgumentList.Add(argument); }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); }
            throw;
        }

        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }

    internal Task<ChangeAnalysisResult> Changes(string reference = "HEAD", CancellationToken? token = null) =>
        new SolutionAnalysisService().AnalyzeChangesAsync(new(Input, true), PolicyPath, reference,
            new YamlPolicyLoader(), cancellationToken: token ?? TestContext.Current.CancellationToken);

    internal async Task<AnalysisResult> FullScan()
    {
        var restore = await Repository.Dotnet(TimeSpan.FromSeconds(60), "restore", Input, "--source", Root,
            "--disable-parallel", "--disable-build-servers");
        Assert.True(restore.ExitCode == 0, restore.Output + restore.Error);
        var policy = await new YamlPolicyLoader().LoadAsync(PolicyPath, TestContext.Current.CancellationToken);
        var result = await new SolutionAnalysisService().AnalyzeAsync(new(Input, true), Root, policy, TestContext.Current.CancellationToken);
        Assert.True(result.IsComplete, string.Join('\n', result.Issues.Select(item => item.Message)));
        return result;
    }

    /// <summary>Removes only this fixture's repository and generated assets.</summary>
    public void Dispose()
    {
        // Git objects may be read-only on Windows.
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) { File.SetAttributes(file, FileAttributes.Normal); }
        Directory.Delete(Root, recursive: true);
    }
}
