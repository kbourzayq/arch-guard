using ArchitectureGuardian.Cli;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Cli;

/// <summary>Verifies parsing and early cancellation without executing repository build logic.</summary>
public sealed class CliCommandTests
{
    /// <summary>Checks local catalog lookup, including IDs whose implementations are still planned.</summary>
    /// <param name="id">A canonical rule ID.</param>
    [Theory]
    [InlineData("AG001")]
    [InlineData("AG002")]
    [InlineData("AG003")]
    [InlineData("AG004")]
    [InlineData("AG005")]
    [InlineData("AG006")]
    public async Task Explain_resolves_canonical_catalog(string id)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await CliApplication.RunAsync(["explain", id], output, error);

        Assert.Equal(0, code);
        Assert.StartsWith(id + ":", output.ToString());
        Assert.Contains("Documentation: https://github.com/kbourzayq/arch-guard/", output.ToString());
        Assert.Empty(error.ToString());
        if (id is "AG006")
        {
            Assert.Contains("not yet available", output.ToString());
        }
        else
        {
            Assert.Contains("Available in scan", output.ToString());
        }
    }

    /// <summary>Rejects missing, unknown, repeated or blank values before evaluating a workspace.</summary>
    /// <param name="arguments">Invalid command arguments.</param>
    [Theory]
    [InlineData("scan")]
    [InlineData("inspect", "input.csproj")]
    [InlineData("explain")]
    [InlineData("explain", "ag001")]
    [InlineData("explain", "AG007")]
    [InlineData("scan", "input.csproj", "--policy")]
    [InlineData("scan", "input.csproj", "--policy", "policy.yaml", "--unknown")]
    [InlineData("scan", "input.csproj", "--policy", "policy.yaml", "--policy", "other.yaml")]
    [InlineData("load", "input.csproj", "--trusted", "--trusted")]
    [InlineData("load", "input.csproj", "--configuration", "")]
    public async Task Invalid_arguments_return_two(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(2, await CliApplication.RunAsync(arguments, output, error));
        Assert.NotEmpty(error.ToString());
        Assert.Empty(output.ToString());
    }

    /// <summary>Checks command-specific generated help without requiring positional values or policy files.</summary>
    /// <param name="command">The command whose options should be documented.</param>
    [Theory]
    [InlineData("scan")]
    [InlineData("inspect")]
    [InlineData("load")]
    [InlineData("explain")]
    public async Task Command_help_is_available(string command)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, await CliApplication.RunAsync([command, "--help"], output, error));
        Assert.Contains("Usage:", output.ToString());
        Assert.Empty(error.ToString());
    }

    /// <summary>Ensures cancellation produces an actionable error and cannot report successful work.</summary>
    [Fact]
    public async Task Cancellation_returns_two_without_starting_work()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Equal(2, await CliApplication.RunAsync(
            ["scan", "input.csproj", "--policy", "missing.yaml", "--trusted"], output, error, source.Token));
        Assert.Contains("cancelled", error.ToString());
        Assert.Contains("rerun", error.ToString());
        Assert.Empty(output.ToString());
    }
}
