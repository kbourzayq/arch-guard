using System.Diagnostics;
using Xunit;

namespace ArchitectureGuardian.Tests.Workspaces;

/// <summary>Copies and restores isolated workspace fixtures without sharing intermediate outputs with the checkout.</summary>
public sealed class WorkspaceFixture : IAsyncLifetime
{
    /// <summary>Gets the isolated fixture root.</summary>
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "arch-guard-workspaces-" + Guid.NewGuid().ToString("N"));

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
        await RestoreAsync("Multi/Multi.csproj");
    }

    /// <summary>Runs a bounded restore using direct process arguments and captures failures.</summary>
    /// <param name="input">The fixture-relative project or solution path.</param>
    /// <param name="expectedSuccess">Whether restore must succeed.</param>
    /// <returns>The combined restore output for assertions.</returns>
    public async Task<string> RestoreAsync(string input, bool expectedSuccess = true)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "restore", Path.Combine(Root, input), "--ignore-failed-sources", "--disable-parallel" })
        {
            start.ArgumentList.Add(argument);
        }

        if (!expectedSuccess)
        {
            var emptySource = Path.Combine(Root, "empty-feed");
            Directory.CreateDirectory(emptySource);
            start.ArgumentList.Add("--source");
            start.ArgumentList.Add(emptySource);
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
            throw new TimeoutException("Fixture restore timed out.");
        }

        var text = await output + await error;
        Assert.True((process.ExitCode == 0) == expectedSuccess, text);
        return text;
    }

    /// <summary>Removes the temporary fixture and all of its intermediate outputs.</summary>
    /// <returns>A completed cleanup task.</returns>
    public Task DisposeAsync()
    {
        Directory.Delete(Root, recursive: true);
        return Task.CompletedTask;
    }
}
