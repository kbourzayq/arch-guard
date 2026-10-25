using System.Text.Json;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using ArchitectureGuardian.Cli;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Cli;

/// <summary>Exercises machine output through CLI composition, including Git comparison and operational failures.</summary>
[Collection(nameof(CliWorkspaceTests))]
public sealed class CliReportTests
{
    /// <summary>Partial reports stay parseable and report-write errors override analysis outcomes with exit two.</summary>
    /// <param name="format">The requested machine encoding.</param>
    [Theory]
    [InlineData("json")]
    [InlineData("sarif")]
    public async Task Incomplete_reports_and_write_failures_have_operational_outcomes(string format)
    {
        using var files = new ChangeInputFiles();
        using var output = new StringWriter();
        using var error = new StringWriter();
        string[] arguments = ["scan", files.Input, "--policy", files.PolicyPath, "--format", format];
        Assert.Equal(2, await CliApplication.RunAsync(arguments, output, error, TestContext.Current.CancellationToken));
        using var document = JsonDocument.Parse(output.ToString());
        var envelope = format == "json" ? document.RootElement : document.RootElement.GetProperty("runs")[0].GetProperty("properties");
        Assert.Equal("incomplete", envelope.GetProperty("status").GetString());
        Assert.Equal("notEvaluated", envelope.GetProperty("gateOutcome").GetString());
        Assert.Contains("workspace-untrusted", output.ToString());
        Assert.NotEmpty(error.ToString());

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        var path = Path.Combine(files.Root, "report");
        Assert.Equal(2, await CliApplication.RunAsync([.. arguments, "--output", path], output, error, TestContext.Current.CancellationToken));
        Assert.Empty(output.ToString());
        using var written = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(document.RootElement.GetRawText(), written.RootElement.GetRawText());

        var directory = Path.Combine(files.Root, "destination");
        Directory.CreateDirectory(directory);
        foreach (var destination in new[] { Path.Combine(files.Root, "missing", "report"), directory })
        {
            error.GetStringBuilder().Clear();
            Assert.Equal(2, await CliApplication.RunAsync([.. arguments, "--output", destination], output, error, TestContext.Current.CancellationToken));
            Assert.Empty(output.ToString());
            Assert.Contains("Invalid command input", error.ToString());
            Assert.Empty(Directory.GetFiles(files.Root, "*.tmp"));
        }
        Assert.True(Directory.Exists(directory));
        Assert.Equal(document.RootElement.GetRawText(), (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Trim());
    }

    /// <summary>Both snapshots' coverage and baseline classifications survive machine output through the Git CLI path.</summary>
    /// <param name="format">The requested machine encoding.</param>
    [Theory]
    [InlineData("json")]
    [InlineData("sarif")]
    [Trait("Category", "Integration")]
    public async Task Git_comparison_reports_existing_and_new_findings(string format)
    {
        using var files = new ChangeInputFiles();
        files.AddReference();
        files.Write("App/Existing.cs", "public class Existing : Library.Target { }");
        await files.Initialize();
        files.Write("policy.yaml", ChangeInputFiles.Policy.Replace("AG001: error", "AG001: error\n  AG005: error", StringComparison.Ordinal)
            .Replace("  - id: App\n", "  - id: App\n    forbiddenNamespaces: [Library]\n", StringComparison.Ordinal));
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, await CliApplication.RunAsync(["scan", files.Input, "--policy", files.PolicyPath,
            "--base", "HEAD", "--trusted", "--format", format], output, error, TestContext.Current.CancellationToken));
        Assert.Empty(error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        var envelope = format == "json" ? document.RootElement : document.RootElement.GetProperty("runs")[0].GetProperty("properties");
        Assert.Equal("completed", envelope.GetProperty("status").GetString());
        Assert.Equal("failed", envelope.GetProperty("gateOutcome").GetString());
        Assert.Equal((await files.Git("rev-parse", "HEAD")).Trim(), envelope.GetProperty("baseCommit").GetString());
        if (format == "json")
        {
            var diagnostics = envelope.GetProperty("diagnostics").EnumerateArray().ToArray();
            Assert.Equal(2, diagnostics.Length);
            Assert.Contains(diagnostics, item => item.GetProperty("baselineState").GetString() == "existing");
            Assert.Contains(diagnostics, item => item.GetProperty("baselineState").GetString() == "new");
        }
        else
        {
            var results = document.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray().ToArray();
            Assert.Equal(2, results.Length);
            Assert.Contains(results, item => item.GetProperty("baselineState").GetString() == "unchanged");
            Assert.Contains(results, item => item.GetProperty("baselineState").GetString() == "new");
        }
    }
}
