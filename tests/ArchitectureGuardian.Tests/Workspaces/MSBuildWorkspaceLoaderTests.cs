using System.Reflection;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Analysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ArchitectureGuardian.Tests.Workspaces;

/// <summary>Exercises real SDK design-time builds and verifies coverage, build context and cancellation.</summary>
/// <param name="fixture">Restored, isolated workspace inputs shared within this test class.</param>
public sealed class MSBuildWorkspaceLoaderTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    /// <summary>Checks every supported input format and source-based project reference loading.</summary>
    /// <param name="input">The solution or project fixture.</param>
    [Theory]
    [InlineData("Fixture.sln")]
    [InlineData("Fixture.slnx")]
    [InlineData("App/App.csproj")]
    public async Task Supported_inputs_load_complete_context(string input)
    {
        using var result = await LoadAsync(input);

        AssertComplete(result);
        Assert.Equal(2, result.Solution!.ProjectIds.Count);
        var app = Assert.Single(result.Solution.Projects, project => project.Name == "App");
        Assert.Single(app.ProjectReferences);
        var compilation = await app.GetCompilationAsync();
        Assert.NotNull(compilation!.GetTypeByMetadataName("Library.IContract"));
    }

    /// <summary>Checks evaluated source inclusion and semantic symbols in both build configurations.</summary>
    /// <param name="configuration">The build configuration to load.</param>
    /// <param name="expectedType">The type present under that configuration's compilation symbols.</param>
    [Theory]
    [InlineData("Debug", "DebugContract")]
    [InlineData("Release", "ReleaseContract")]
    public async Task Configuration_controls_documents_and_conditional_compilation(string configuration, string expectedType)
    {
        using var result = await LoadAsync("App/App.csproj", configuration: configuration);

        AssertComplete(result);
        var app = Assert.Single(result.Solution!.Projects, project => project.Name == "App");
        var compilation = await app.GetCompilationAsync();
        Assert.NotNull(compilation!.GetTypeByMetadataName("App." + expectedType));
        Assert.Equal(configuration == "Release", app.Documents.Any(document => document.Name == "ReleaseOnly.cs"));
        Assert.Equal(configuration == "Release", ((CSharpParseOptions)app.ParseOptions!).PreprocessorSymbolNames.Contains("RELEASE_ONLY"));
    }

    /// <summary>Prevents an implicitly selected target from claiming full multi-target coverage.</summary>
    [Fact]
    public async Task Multiple_frameworks_require_an_explicit_selection()
    {
        using var result = await LoadAsync("Multi/Multi.csproj");

        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code == "framework-required");
    }

    /// <summary>Checks that each explicitly selected framework exposes its own semantic context.</summary>
    /// <param name="framework">The requested framework.</param>
    /// <param name="expectedType">The type present only in this framework's compilation.</param>
    [Theory]
    [InlineData("net10.0", "Net10Contract")]
    [InlineData("netstandard2.1", "StandardContract")]
    public async Task Explicit_framework_selects_the_matching_compilation(string framework, string expectedType)
    {
        using var result = await LoadAsync("Multi/Multi.csproj", framework: framework);

        AssertComplete(result);
        var compilation = await Assert.Single(result.Solution!.Projects).GetCompilationAsync();
        Assert.NotNull(compilation!.GetTypeByMetadataName("Multi." + expectedType));
    }

    /// <summary>Rejects a framework that is not declared even if MSBuild can synthesize a context for it.</summary>
    [Fact]
    public async Task Undeclared_framework_is_incomplete()
    {
        using var result = await LoadAsync("App/App.csproj", framework: "netstandard2.1");

        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code == "framework-unavailable");
    }

    /// <summary>Ensures missing SDKs, missing inputs, unsupported extensions and unavailable references retain useful issues.</summary>
    /// <param name="input">The invalid or incomplete fixture.</param>
    [Theory]
    [InlineData("MissingSdk/MissingSdk.csproj")]
    [InlineData("MissingReference/MissingReference.csproj")]
    [InlineData("Unavailable.csproj")]
    [InlineData("NuGet.Config")]
    public async Task Loading_problems_are_incomplete_and_explained(string input)
    {
        using var result = await LoadAsync(input);

        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.NotEmpty(result.Issues);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("Sdk", StringComparison.OrdinalIgnoreCase) ||
            issue.Message.Contains("Unavailable", StringComparison.OrdinalIgnoreCase) ||
            issue.Message.Contains(".csproj", StringComparison.Ordinal));
    }

    /// <summary>Ensures a failed package restore cannot become a successful empty compilation.</summary>
    [Fact]
    public async Task Failed_restore_is_incomplete()
    {
        Assert.Contains("ArchitectureGuardian.Nonexistent.Package", await fixture.RestoreAsync("FailedRestore/FailedRestore.csproj", expectedSuccess: false));
        using var result = await LoadAsync("FailedRestore/FailedRestore.csproj");

        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.NotEmpty(result.Issues);
    }

    /// <summary>Ensures missing restore assets produce a targeted recovery message.</summary>
    [Fact]
    public async Task Unrestored_project_requires_restore()
    {
        var directory = Path.Combine(fixture.Root, "Unrestored");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "Unrestored.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        using var result = await LoadAsync("Unrestored/Unrestored.csproj");

        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code == "restore-required");
        Assert.False(File.Exists(Path.Combine(directory, "obj", "project.assets.json")));
    }

    /// <summary>Rejects unavailable assembly references even when no source happens to use their types.</summary>
    [Fact]
    public async Task Unused_missing_metadata_reference_is_incomplete()
    {
        var directory = Path.Combine(fixture.Root, "MissingMetadata");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "MissingMetadata.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><Reference Include=\"Unavailable.Assembly\"><HintPath>unavailable.dll</HintPath></Reference></ItemGroup></Project>");
        await fixture.RestoreAsync("MissingMetadata/MissingMetadata.csproj");
        using var result = await LoadAsync("MissingMetadata/MissingMetadata.csproj");

        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("Unavailable.Assembly", StringComparison.Ordinal));
    }

    /// <summary>Checks solution globals and design-time defaults against a real build and the CLI coverage outcome.</summary>
    /// <param name="input">The solution format whose workspace properties must be preserved.</param>
    [Theory]
    [InlineData("SolutionReference.sln")]
    [InlineData("SolutionReference.slnx")]
    public async Task Reference_validation_uses_solution_and_design_time_properties(string input)
    {
        var build = await fixture.DotnetAsync("build", input, "--configuration", "Release", "--no-restore");
        Assert.True(build.ExitCode == 0, build.Output + build.Error);
        // SDK patches can change unresolved-reference warnings; assert the actual evaluated fixture items.
        Assert.Contains("Fixture solution references:", build.Output);
        Assert.Contains("SolutionScoped.Unavailable.Assembly", build.Output);

        using var result = await LoadAsync(input, configuration: "Release");

        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code == "reference-unavailable" && issue.Message.Contains("SolutionScoped.Unavailable.Assembly", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Code == "reference-unavailable" && issue.Message.Contains("DesignTime.Unavailable.Assembly", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Issues, issue => issue.Code == "compilation-error");
        var cli = await RunCliAsync(input, "Release");
        Assert.Equal(2, cli.ExitCode);
        Assert.Empty(cli.Output);
        Assert.Contains("SolutionScoped.Unavailable.Assembly", cli.Error);
        using var standalone = await LoadAsync("SolutionReference/SolutionReference.csproj", configuration: "Release");
        AssertComplete(standalone);
    }

    /// <summary>Checks generator-reported errors, exceptions, ordinary warnings and successful generated context.</summary>
    /// <param name="mode">The fixture generator behavior selected through analyzer configuration.</param>
    /// <param name="isComplete">Whether this generator outcome establishes full context.</param>
    [Theory]
    [InlineData("valid", true)]
    [InlineData("warning", true)]
    [InlineData("error", false)]
    [InlineData("throw", false)]
    public async Task Generator_diagnostics_contribute_to_coverage(string mode, bool isComplete)
    {
        var path = Path.Combine(fixture.Root, "GeneratorConsumer", "GeneratorConsumer.csproj");
        var source = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, source.Replace("<FixtureGeneratorMode>valid</FixtureGeneratorMode>",
            $"<FixtureGeneratorMode>{mode}</FixtureGeneratorMode>", StringComparison.Ordinal));
        try
        {
            var build = await fixture.DotnetAsync("build", path, "--no-restore", "--no-incremental");
            Assert.Equal(mode == "error" ? 1 : 0, build.ExitCode);
            using var result = await LoadAsync("GeneratorConsumer/GeneratorConsumer.csproj");

            Assert.Equal(isComplete ? AnalysisStatus.Completed : AnalysisStatus.Incomplete, result.Status);
            Assert.DoesNotContain(result.Issues, issue => issue.Code == "compilation-error");
            if (isComplete)
            {
                AssertComplete(result);
                var compilation = await Assert.Single(result.Solution!.Projects).GetCompilationAsync();
                Assert.NotNull(compilation!.GetTypeByMetadataName("Generated.Contract"));
            }
            else
            {
                var issue = Assert.Single(result.Issues, issue => issue.Code == (mode == "error" ? "source-generator-error" : "source-generator-failed"));
                Assert.Contains(mode == "error" ? "FIXTURE001" : "fixture generator failed", issue.Message);
            }

            var cli = await RunCliAsync("GeneratorConsumer/GeneratorConsumer.csproj");
            Assert.Equal(isComplete ? 0 : 2, cli.ExitCode);
            Assert.Equal(isComplete, string.IsNullOrEmpty(cli.Error));
            Assert.Equal(!isComplete, string.IsNullOrEmpty(cli.Output));
        }
        finally
        {
            await File.WriteAllTextAsync(path, source);
        }
    }

    /// <summary>Prevents malformed or empty solution inputs from looking like successful clean loads.</summary>
    /// <param name="contents">The invalid or empty solution XML.</param>
    [Theory]
    [InlineData("<Solution />")]
    [InlineData("<Solution>")]
    public async Task Empty_or_malformed_solution_is_incomplete(string contents)
    {
        var input = "Invalid-" + Guid.NewGuid().ToString("N") + ".slnx";
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, input), contents);
        using var result = await LoadAsync(input);

        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.NotEmpty(result.Issues);
    }

    /// <summary>Rejects untrusted inputs before evaluating project content.</summary>
    [Fact]
    public async Task Trust_is_required_before_any_loading()
    {
        using var result = await new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(fixture.Root, "MissingSdk/MissingSdk.csproj")));

        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Equal("workspace-untrusted", Assert.Single(result.Issues).Code);
        Assert.Null(result.Solution);
    }

    /// <summary>Checks pre-requested and in-flight cancellation rather than converting cancellation to clean coverage.</summary>
    /// <param name="beforeOpening">Whether to cancel before invoking the loader.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancellation_never_reports_complete_loading(bool beforeOpening)
    {
        using var cancellation = new CancellationTokenSource();
        if (beforeOpening)
        {
            cancellation.Cancel();
        }

        var task = new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(fixture.Root, "Fixture.slnx"), true), cancellation.Token);
        cancellation.Cancel();
        using var result = await task;

        Assert.Equal(AnalysisStatus.Cancelled, result.Status);
        if (beforeOpening)
        {
            Assert.Null(result.Solution);
        }
    }

    private Task<WorkspaceLoadResult> LoadAsync(string input, string configuration = "Debug", string? framework = null) =>
        new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(fixture.Root, input), true, configuration, framework));

    private Task<(int ExitCode, string Output, string Error)> RunCliAsync(string input, string configuration = "Debug")
    {
        var buildConfiguration = typeof(MSBuildWorkspaceLoaderTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var cli = Path.Combine(fixture.RepositoryRoot, "src", "ArchitectureGuardian.Cli", "bin", buildConfiguration, "net10.0", "ArchitectureGuardian.Cli.dll");
        return fixture.DotnetAsync(cli, "load", Path.Combine(fixture.Root, input), "--trusted", "--configuration", configuration);
    }

    private static void AssertComplete(WorkspaceLoadResult result) =>
        Assert.True(result.Status == AnalysisStatus.Completed, string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
}
