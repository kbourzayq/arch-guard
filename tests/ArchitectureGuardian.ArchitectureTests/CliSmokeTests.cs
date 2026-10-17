using System.Reflection;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests;

/// <summary>Exercises the built CLI as a process to verify its public output and exit-code contract.</summary>
public sealed class CliSmokeTests
{
    /// <summary>Checks that no arguments and both help switches print usage successfully without errors.</summary>
    /// <param name="argument">The help argument, or null to invoke the CLI without arguments.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task Help_is_available(string? argument)
    {
        var result = await RunCli(argument is null ? [] : [argument]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage: arch-guard", result.Output);
        Assert.Empty(result.Error);
    }

    /// <summary>Checks that the CLI reports a three-part assembly version successfully.</summary>
    [Fact]
    public async Task Version_is_available()
    {
        var result = await RunCli("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"^Architecture Guardian \d+\.\d+\.\d+\s*$", result.Output);
        Assert.Empty(result.Error);
    }

    /// <summary>Checks that unsupported argument combinations fail explicitly instead of implying a successful scan.</summary>
    /// <param name="first">The first unsupported argument.</param>
    /// <param name="second">An optional second argument used to exercise invalid combinations.</param>
    [Theory]
    [InlineData("scan", null)]
    [InlineData("--unknown", null)]
    [InlineData("--help", "scan")]
    public async Task Unsupported_commands_do_not_report_success(string first, string? second)
    {
        var result = await RunCli(second is null ? [first] : [first, second]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Unknown arguments:", result.Error);
        Assert.Empty(result.Output);
    }

    /// <summary>Runs the CLI assembly built in the same configuration as the current tests.</summary>
    /// <param name="arguments">Arguments to pass directly to the CLI process.</param>
    /// <returns>The CLI's exit code and captured standard output and error streams.</returns>
    private static Task<CommandResult> RunCli(params string[] arguments)
    {
        var configuration = typeof(CliSmokeTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var assembly = Path.Combine(Repository.Root, "src", "ArchitectureGuardian.Cli", "bin", configuration,
            "net10.0", "ArchitectureGuardian.Cli.dll");
        return Repository.Dotnet([assembly, .. arguments]);
    }
}
