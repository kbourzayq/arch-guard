using System.Text.Json;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Dependencies;

/// <summary>Protects the compiler baseline and independent fixture restores under central package management.</summary>
[Trait("Category", "Integration")]
public sealed class CentralPackageManagementTests
{
    /// <summary>Checks the effective central version and the restored analyzer-test compiler baseline.</summary>
    [Fact]
    public async Task Analyzer_tests_retain_the_Roslyn_4_8_baseline()
    {
        const string project = "tests/ArchitectureGuardian.Analyzers.Tests/ArchitectureGuardian.Analyzers.Tests.csproj";
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var result = await Repository.Dotnet("msbuild", project, "-nologo", "-property:Configuration=" + configuration,
                "-getProperty:ManagePackageVersionsCentrally", "-getItem:PackageVersion");
            Assert.True(result.ExitCode == 0, result.Output + result.Error);
            using var document = JsonDocument.Parse(result.Output);
            Assert.Equal("true", document.RootElement.GetProperty("Properties").GetProperty("ManagePackageVersionsCentrally").GetString());
            var version = document.RootElement.GetProperty("Items").GetProperty("PackageVersion").EnumerateArray()
                .Single(item => item.GetProperty("Identity").GetString() == "Microsoft.CodeAnalysis.CSharp.Workspaces");
            Assert.Equal("4.8.0", version.GetProperty("Version").GetString());
        }

        using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root,
            "tests", "ArchitectureGuardian.Analyzers.Tests", "obj", "project.assets.json")));
        Assert.True(assets.RootElement.GetProperty("libraries").TryGetProperty("Microsoft.CodeAnalysis.CSharp.Workspaces/4.8.0", out _));
        Assert.True(assets.RootElement.GetProperty("libraries").TryGetProperty("Microsoft.CodeAnalysis.CSharp/4.8.0", out _));
        Assert.True(assets.RootElement.GetProperty("libraries").TryGetProperty("Microsoft.CodeAnalysis.Common/4.8.0", out _));
    }

    /// <summary>Checks that fixture roots opt out rather than inheriting central package declarations.</summary>
    /// <param name="project">A project under an independently restored or copied fixture root.</param>
    [Theory]
    [InlineData("Workspaces/Generators/FixtureGenerator.csproj")]
    [InlineData("ReferenceSolutions/CleanArchitecture/Shop.Domain/Shop.Domain.csproj")]
    [InlineData("AgentCorrection/Consumer/Consumer.csproj")]
    public async Task Fixture_roots_are_independent_of_central_versions(string project)
    {
        var result = await Repository.Dotnet("msbuild", Path.Combine("tests", "Fixtures", project), "-nologo",
            "-getProperty:ManagePackageVersionsCentrally");
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Equal("false", result.Output.Trim());
    }
}
