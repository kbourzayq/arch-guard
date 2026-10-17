using System.Diagnostics;
using System.Xml.Linq;
using Xunit;

namespace ArchitectureGuardian.Tests.Workspaces;

/// <summary>Copies and restores isolated workspace fixtures without sharing intermediate outputs with the checkout.</summary>
public sealed class WorkspaceFixture : IAsyncLifetime
{
    /// <summary>Gets the isolated fixture root.</summary>
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "arch-guard-workspaces-" + Guid.NewGuid().ToString("N"));

    /// <summary>Gets the checkout containing the built CLI and the checked-in fixtures.</summary>
    public string RepositoryRoot { get; private set; } = "";

    /// <summary>Copies checked-in inputs and restores the supported fixture contexts.</summary>
    /// <returns>A task completing when restore prerequisites are available.</returns>
    public async Task InitializeAsync()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "ArchitectureGuardian.slnx")))
        {
            repository = repository.Parent;
        }

        Assert.NotNull(repository);
        RepositoryRoot = repository.FullName;
        var source = Path.Combine(repository.FullName, "tests", "Fixtures", "Workspaces");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            {
                continue;
            }

            var destination = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        File.Copy(Path.Combine(repository.FullName, "global.json"), Path.Combine(Root, "global.json"));
        await RestoreAsync("Fixture.slnx");
        await RestoreAsync("ModularMonolith/ModularMonolith.slnx");
        await RestoreAsync("Multi/Multi.csproj");
        await RestoreAsync("SolutionReference.slnx");
        await RestoreAsync("Generators/FixtureGenerator.csproj");
        await RestoreAsync("GeneratorConsumer/GeneratorConsumer.csproj");
        var build = await DotnetAsync("build", "Generators/FixtureGenerator.csproj", "--configuration", "Debug", "--no-restore");
        Assert.True(build.ExitCode == 0, build.Output + build.Error);

        // Roslyn loads generators into a process-lifetime context and can lock their DLLs on Windows.
        // Keep the loaded binary with test build artifacts so the temporary fixture can still be deleted.
        var generatorDirectory = Path.Combine(AppContext.BaseDirectory, "WorkspaceFixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(generatorDirectory);
        var generatorPath = Path.Combine(generatorDirectory, "FixtureGenerator.dll");
        File.Copy(Path.Combine(Root, "Generators", "bin", "Debug", "netstandard2.0", "FixtureGenerator.dll"), generatorPath);
        var consumerPath = Path.Combine(Root, "GeneratorConsumer", "GeneratorConsumer.csproj");
        var consumer = XDocument.Load(consumerPath);
        consumer.Descendants("Analyzer").Single().SetAttributeValue("Include", generatorPath);
        consumer.Save(consumerPath);
    }

    /// <summary>Runs a bounded restore using direct process arguments and captures failures.</summary>
    /// <param name="input">The fixture-relative or absolute project or solution path.</param>
    /// <param name="expectedSuccess">Whether restore must succeed.</param>
    /// <returns>The combined restore output for assertions.</returns>
    public async Task<string> RestoreAsync(string input, bool expectedSuccess = true)
    {
        var arguments = new List<string> { "restore", Path.Combine(Root, input), "--ignore-failed-sources", "--disable-parallel" };
        if (!expectedSuccess)
        {
            var emptySource = Path.Combine(Root, "empty-feed");
            Directory.CreateDirectory(emptySource);
            arguments.AddRange(["--source", emptySource]);
        }

        var result = await DotnetAsync(arguments.ToArray());
        var text = result.Output + result.Error;
        Assert.True((result.ExitCode == 0) == expectedSuccess, text);
        return text;
    }

    /// <summary>Runs a bounded .NET command in the isolated fixture directory and captures both streams.</summary>
    /// <param name="arguments">Arguments passed without shell interpretation.</param>
    /// <returns>The exit code and separate standard output and error streams.</returns>
    public async Task<(int ExitCode, string Output, string Error)> DotnetAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // Reused build workers can retain redirected output handles after the command exits.
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Fixture .NET command timed out.");
        }

        return (process.ExitCode, await output, await error);
    }

    /// <summary>Removes the temporary fixture and all of its intermediate outputs.</summary>
    /// <returns>A completed cleanup task.</returns>
    public Task DisposeAsync()
    {
        Directory.Delete(Root, recursive: true);
        return Task.CompletedTask;
    }
}
