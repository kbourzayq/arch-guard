using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Infrastructure;

/// <summary>Checks bounded process termination and output capture in the integration harness.</summary>
public sealed class RepositoryProcessTests
{
    /// <summary>An expired command terminates promptly and does not prevent the next command from capturing both streams.</summary>
    [Fact]
    public async Task Deadline_terminates_command_and_subsequent_process_still_completes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "arch-guard-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, "Wait.proj");
        var release = Path.Combine(directory, "release");
        try
        {
            // The child cannot complete until the test releases it, regardless of parent scheduling.
            await File.WriteAllTextAsync(project, """
                <Project>
                  <UsingTask TaskName="WaitForRelease" TaskFactory="RoslynCodeTaskFactory"
                             AssemblyFile="$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll">
                    <ParameterGroup><ReleasePath ParameterType="System.String" Required="true" /></ParameterGroup>
                    <Task><Code Type="Fragment" Language="cs"><![CDATA[
                      while (!System.IO.File.Exists(ReleasePath)) { System.Threading.Thread.Sleep(20); }
                    ]]></Code></Task>
                  </UsingTask>
                  <Target Name="Wait"><WaitForRelease ReleasePath="$(MSBuildProjectDirectory)/release" /></Target>
                </Project>
                """);
            // Verify that the fixture itself is valid before asking it to block.
            await File.WriteAllTextAsync(release, "release");
            var ready = await Repository.Dotnet(TimeSpan.FromSeconds(60), "msbuild", project, "-nologo", "-target:Wait");
            Assert.True(ready.ExitCode == 0, ready.Output + ready.Error);
            File.Delete(release);
            var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
                Repository.Dotnet(TimeSpan.FromMilliseconds(1), "msbuild", project, "-nologo", "-target:Wait"));
            Assert.Contains("timed out", exception.Message);
            var version = await Repository.Dotnet("--version");
            Assert.Equal(0, version.ExitCode);
            Assert.NotEmpty(version.Output);
            Assert.Empty(version.Error);
        }
        finally
        {
            await File.WriteAllTextAsync(release, "release");
            Directory.Delete(directory, recursive: true);
        }
    }
}
