using System.Text.Json;
using System.Xml.Linq;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Dependencies;

/// <summary>Enforces solution membership, inward project dependencies and Core's independence from adapters.</summary>
[Trait("Category", "Integration")]
public sealed class DependencyBoundaryTests
{
    // Adding an adapter requires an explicit dependency decision here and in ADR 0001.
    private static readonly Dictionary<string, string[]> ProjectDependencies = new()
    {
        ["ArchitectureGuardian.Core"] = [],
        ["ArchitectureGuardian.Configuration"] = ["ArchitectureGuardian.Core"],
        ["ArchitectureGuardian.Analysis"] = ["ArchitectureGuardian.Core"],
        ["ArchitectureGuardian.Cli"] = ["ArchitectureGuardian.Analysis"],
    };

    /// <summary>Requires every production project to have an explicit dependency decision and belong to the solution.</summary>
    [Fact]
    public void Every_source_project_has_an_explicit_boundary_and_is_in_the_solution()
    {
        var sourceProjects = Directory.GetFiles(Path.Combine(Repository.Root, "src"), "*.csproj", SearchOption.AllDirectories);
        var solution = XDocument.Load(Path.Combine(Repository.Root, "ArchitectureGuardian.slnx"));
        var solutionProjects = solution.Descendants("Project")
            .Select(project => Path.GetFullPath(project.Attribute("Path")!.Value, Repository.Root))
            .ToHashSet();

        Assert.Equal(ProjectDependencies.Keys.Order(), sourceProjects.Select(Path.GetFileNameWithoutExtension).Order());
        Assert.All(sourceProjects, path => Assert.Contains(path, solutionProjects));
    }

    /// <summary>Checks evaluated references so imported or configuration-specific dependencies cannot bypass the boundaries.</summary>
    /// <param name="project">The production project whose references are inspected.</param>
    /// <param name="configuration">The build configuration in which MSBuild evaluates the project.</param>
    [Theory]
    [InlineData("ArchitectureGuardian.Core", "Debug")]
    [InlineData("ArchitectureGuardian.Core", "Release")]
    [InlineData("ArchitectureGuardian.Configuration", "Debug")]
    [InlineData("ArchitectureGuardian.Configuration", "Release")]
    [InlineData("ArchitectureGuardian.Analysis", "Debug")]
    [InlineData("ArchitectureGuardian.Analysis", "Release")]
    [InlineData("ArchitectureGuardian.Cli", "Debug")]
    [InlineData("ArchitectureGuardian.Cli", "Release")]
    public async Task Evaluated_references_respect_dependency_direction(string project, string configuration)
    {
        var path = Path.Combine("src", project, $"{project}.csproj");
        var result = await Repository.Dotnet("msbuild", path, "-nologo", $"-property:Configuration={configuration}",
            "-getProperty:TargetFramework", "-getItem:ProjectReference,PackageReference,Reference,FrameworkReference");

        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal("net10.0", document.RootElement.GetProperty("Properties").GetProperty("TargetFramework").GetString());

        var items = document.RootElement.GetProperty("Items");
        var references = items.GetProperty("ProjectReference").EnumerateArray()
            .Select(item => item.GetProperty("FullPath").GetString()!)
            .Order().ToArray();
        var expected = ProjectDependencies[project]
            .Select(name => Path.Combine(Repository.Root, "src", name, $"{name}.csproj"))
            .Order().ToArray();
        Assert.Equal(expected, references);

        if (project == "ArchitectureGuardian.Core")
        {
            // Evaluate MSBuild items so imports in Directory.Build.* cannot hide dependencies.
            Assert.Empty(items.GetProperty("PackageReference").EnumerateArray());
            Assert.Empty(items.GetProperty("Reference").EnumerateArray());
            Assert.All(items.GetProperty("FrameworkReference").EnumerateArray(), reference =>
            {
                Assert.Equal("Microsoft.NETCore.App", reference.GetProperty("Identity").GetString());
                Assert.Equal("true", reference.GetProperty("IsImplicitlyDefined").GetString());
            });
        }
    }
}
