using System.Reflection;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests;

public sealed class CliSmokeTests
{
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

    [Fact]
    public async Task Version_is_available()
    {
        var result = await RunCli("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"^Architecture Guardian \d+\.\d+\.\d+\s*$", result.Output);
        Assert.Empty(result.Error);
    }

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

    private static Task<CommandResult> RunCli(params string[] arguments)
    {
        var configuration = typeof(CliSmokeTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var assembly = Path.Combine(Repository.Root, "src", "ArchitectureGuardian.Cli", "bin", configuration,
            "net10.0", "ArchitectureGuardian.Cli.dll");
        return Repository.Dotnet([assembly, .. arguments]);
    }
}
