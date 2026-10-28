using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Policy;
using Xunit;
using PortablePattern = ArchitectureGuardian.Policy.Documents.ProjectPattern;

namespace ArchitectureGuardian.Tests.Policies;

/// <summary>Checks portable reader snapshots, source positions and selector equivalence with Core.</summary>
public sealed class PortablePolicyTests
{
    private const string Yaml = """
        version: 1
        modules:
          - id: All
            projects: ["**/*.csproj"]
        layers:
          - id: All
            projects: ["**/*.csproj"]
        rules:
          AG001: error
        """;

    /// <summary>Both hosts receive immutable declarations and the adapter preserves their meaning.</summary>
    [Fact]
    public void Reader_retains_declaration_positions_and_read_only_snapshots()
    {
        var document = new PolicyDocumentReader().Parse(Yaml, cancellationToken: TestContext.Current.CancellationToken);
        var core = new YamlPolicyLoader().Parse(Yaml, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(3, document.Locations["$.modules[0]"].Line);
        Assert.Equal(core.Modules[0].Id, document.Modules[0].Id);
        Assert.Equal(core.SeverityPolicy.RuleSeverities.Keys, document.Rules.Keys);
        Assert.Throws<NotSupportedException>(() => ((IList<ArchitectureGuardian.Policy.Documents.ModulePolicy>)document.Modules).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, ArchitectureGuardian.Policy.Documents.PolicySeverity>)document.Rules).Clear());
    }

    /// <summary>Requested cancellation propagates before parsing or adapter conversion.</summary>
    [Fact]
    public void Reader_propagates_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new PolicyDocumentReader().Parse(Yaml, cancellationToken: cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => new YamlPolicyLoader().Parse(Yaml, cancellationToken: cancellation.Token));
    }

    /// <summary>Both assemblies compile the same bounded portable selector implementation.</summary>
    /// <param name="pattern">The policy selector.</param>
    /// <param name="path">A project identity.</param>
    /// <param name="expected">The expected match.</param>
    [Theory]
    [InlineData("src/**", "src", false)]
    [InlineData("src/**", "src/A.csproj", true)]
    [InlineData("**/*.csproj", "A.csproj", true)]
    [InlineData("src/**/**/*.csproj", "src/A.csproj", true)]
    [InlineData("src/**/A?.csproj", "src/one/two/A1.csproj", true)]
    [InlineData("src/*/A?.csproj", "src/one/two/A1.csproj", false)]
    [InlineData("src/A*.csproj", "src\\a.csproj", false)]
    public void Portable_and_core_selectors_agree(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, new ProjectPattern(pattern).IsMatch(path));
        Assert.Equal(expected, new PortablePattern(pattern).IsMatch(path));
    }
}
