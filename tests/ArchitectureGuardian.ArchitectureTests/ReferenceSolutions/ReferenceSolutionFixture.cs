using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.ReferenceSolutions;

/// <summary>Restores isolated copies of the checked-in M1 reference solutions.</summary>
public sealed class ReferenceSolutionFixture : IAsyncLifetime
{
    /// <summary>Gets the copied inputs, separate from checkout build outputs.</summary>
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "archguard-reference-" + Guid.NewGuid().ToString("N"));

    /// <summary>Copies sources and restores every buildable configuration without external packages.</summary>
    public async ValueTask InitializeAsync()
    {
        var source = Path.Combine(Repository.Root, "tests", "Fixtures", "ReferenceSolutions");
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
        // Incomplete deliberately references an absent assembly and contains unresolved types.
    }

    /// <summary>Deletes the copies and intermediate outputs after the class finishes.</summary>
    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
