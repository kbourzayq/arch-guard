using System.Reflection;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Cli;

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

    /// <summary>Checks trust enforcement and strict load argument parsing before workspace evaluation.</summary>
    /// <param name="arguments">Invalid load arguments or an input lacking explicit trust.</param>
    [Theory]
    [InlineData("load")]
    [InlineData("load", "missing.csproj")]
    [InlineData("load", "missing.csproj", "--trusted")]
    [InlineData("load", "missing.csproj", "--framework")]
    [InlineData("load", "missing.csproj", "--trusted", "--trusted")]
    [InlineData("load", "missing.csproj", "--unknown")]
    public async Task Invalid_or_untrusted_load_returns_two(params string[] arguments)
    {
        var result = await RunCli(arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.NotEmpty(result.Error);
        Assert.Empty(result.Output);
    }

    /// <summary>Verifies that real loading outcomes are mapped to process status and the correct console stream.</summary>
    /// <param name="scenario">A complete context, missing SDK, failed restore or unavailable reference.</param>
    [Theory]
    [InlineData("complete")]
    [InlineData("missing-sdk")]
    [InlineData("missing-sdk-version")]
    [InlineData("failed-restore")]
    [InlineData("missing-reference")]
    public async Task Workspace_coverage_controls_cli_exit_code(string scenario)
    {
        var directory = Path.Combine(Path.GetTempPath(), "arch-guard-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Input.csproj");
            if (scenario == "missing-sdk-version")
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "global.json"),
                    "{\"sdk\":{\"version\":\"99.0.100\",\"rollForward\":\"disable\"}}");
            }
            var sdk = scenario == "missing-sdk" ? "ArchitectureGuardian.Nonexistent.Sdk" : "Microsoft.NET.Sdk";
            var items = scenario switch
            {
                "failed-restore" => "<PackageReference Include=\"ArchitectureGuardian.Nonexistent.Package\" Version=\"0.0.0\" />",
                "missing-reference" => "<Reference Include=\"Unavailable.Assembly\"><HintPath>unavailable.dll</HintPath></Reference>",
                _ => "",
            };
            await File.WriteAllTextAsync(path, $"<Project Sdk=\"{sdk}\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>{items}</ItemGroup></Project>");
            if (scenario == "missing-reference")
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "Consumer.cs"), "public class Consumer : Unavailable.Contract { }");
            }

            if (scenario is not ("missing-sdk" or "missing-sdk-version"))
            {
                var restore = await Repository.Dotnet("restore", path, "--source", directory);
                Assert.Equal(scenario == "failed-restore" ? 1 : 0, restore.ExitCode);
            }

            // SDK discovery and design-time builds need the workspace fixture's budget on shared CI runners.
            var result = await RunCli(TimeSpan.FromSeconds(60), "load", path, "--trusted");

            Assert.Equal(scenario == "complete" ? 0 : 2, result.ExitCode);
            if (scenario == "complete")
            {
                Assert.Contains("Workspace loaded: 1 project(s)", result.Output);
                Assert.Contains("No architecture rules evaluated", result.Output);
                Assert.Empty(result.Error);
            }
            else
            {
                Assert.Contains("Workspace loading incomplete", result.Error);
                Assert.Empty(result.Output);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Runs the CLI assembly built in the same configuration as the current tests.</summary>
    /// <param name="arguments">Arguments to pass directly to the CLI process.</param>
    /// <returns>The CLI's exit code and captured standard output and error streams.</returns>
    private static Task<CommandResult> RunCli(params string[] arguments) => RunCli(TimeSpan.FromSeconds(30), arguments);

    /// <summary>Runs the built CLI with a scenario-specific process deadline.</summary>
    /// <param name="timeoutDuration">The positive process deadline.</param>
    /// <param name="arguments">Arguments to pass directly to the CLI process.</param>
    /// <returns>The CLI's exit code and captured standard output and error streams.</returns>
    private static Task<CommandResult> RunCli(TimeSpan timeoutDuration, params string[] arguments)
    {
        var configuration = typeof(CliSmokeTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var assembly = Path.Combine(Repository.Root, "src", "ArchitectureGuardian.Cli", "bin", configuration,
            "net10.0", "ArchitectureGuardian.Cli.dll");
        return Repository.Dotnet(timeoutDuration, [assembly, .. arguments]);
    }
}
