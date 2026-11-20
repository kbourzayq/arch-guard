using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Infrastructure;

/// <summary>Copies a checked-in fixture root to an isolated directory and restores its labeled configurations.</summary>
/// <remarks>
/// The fixture root must contain <c>Preparation.proj</c>, whose <c>Restore</c> target restores every buildable
/// configuration without external packages. Build outputs from the checkout are never copied.
/// </remarks>
/// <param name="fixtureName">The directory below <c>tests/Fixtures</c>.</param>
/// <param name="prefix">The temporary directory name prefix.</param>
public abstract class CopiedSolutionFixture(string fixtureName, string prefix) : IAsyncLifetime
{
    /// <summary>Gets the copied inputs, separate from checkout build outputs.</summary>
    public string Root { get; } = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));

    /// <summary>Copies sources and restores every buildable configuration without external packages.</summary>
    /// <returns>A task that completes when the copy is restored.</returns>
    public async ValueTask InitializeAsync()
    {
        var source = Path.Combine(Repository.Root, "tests", "Fixtures", fixtureName);
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

        File.Copy(Path.Combine(Repository.Root, "global.json"), Path.Combine(Root, "global.json"));
        var restore = await Repository.Dotnet(TimeSpan.FromSeconds(60), "msbuild", Path.Combine(Root, "Preparation.proj"),
            "-target:Restore", "-maxcpucount:1", "-nodeReuse:false");
        Assert.True(restore.ExitCode == 0, restore.Output + restore.Error);
    }

    /// <summary>Deletes the copies and intermediate outputs after the class finishes.</summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
