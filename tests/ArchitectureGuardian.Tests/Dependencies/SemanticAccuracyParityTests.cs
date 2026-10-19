using System.Collections.Immutable;
using ArchitectureGuardian.Analysis.Dependencies;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Analyzers;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Rules;
using ArchitectureGuardian.Tests.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using Severity = ArchitectureGuardian.Core.Diagnostics.DiagnosticSeverity;

namespace ArchitectureGuardian.Tests.Dependencies;

/// <summary>Compares labeled semantic decisions and explicit coverage limits over real MSBuild compilations.</summary>
/// <param name="fixture">Isolated, restored production-shaped inputs.</param>
[Trait("Category", "Integration")]
public sealed class SemanticAccuracyParityTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private static readonly string[] SharedRules = ["AG001", "AG002", "AG004", "AG005", "AG006"];
    private static readonly string[] ForbiddenCases = ["attribute", "base", "interface", "constraint", "alias", "array-alias",
        "generic-alias", "generic-array", "creation", "typeof", "nameof", "cast", "pattern", "member", "extension",
        "operator-signature", "generated"];

    /// <summary>Requires exact expected sets in both engines, including negative cases and overlapping restrictions.</summary>
    /// <param name="configuration">The evaluated build configuration.</param>
    /// <param name="framework">One explicit target framework.</param>
    [Theory]
    [InlineData("Debug", "net10.0")]
    [InlineData("Release", "net10.0")]
    [InlineData("Debug", "netstandard2.1")]
    [InlineData("Release", "netstandard2.1")]
    public async Task Shared_semantics_match_exact_labeled_expectations(string configuration, string framework)
    {
        var root = Path.Combine(fixture.Root, "SemanticParity");
        using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(new(
            Path.Combine(root, "SemanticParity.slnx"), isTrusted: true, configuration: configuration, targetFramework: framework));
        Complete(workspace.Status, workspace.Issues);
        var policy = await new YamlPolicyLoader().LoadAsync(Path.Combine(root, "architecture.guardian.yaml"));
        var context = await new DependencyGraphBuilder().BuildAsync(workspace, root, policy);
        Complete(context.Status, context.Issues);
        var results = await EvaluateAsync(context);
        var compiler = await AnalyzeAsync(workspace.Solution!, await File.ReadAllTextAsync(Path.Combine(root, "architecture.guardian.compiler.xml")));
        var labels = Labels(root);

        var solutionCases = results.SelectMany(result => result.Violations.SelectMany(violation => violation.Evidence
            .Where(fact => fact.Location is not null)
            .Select(fact => (violation.Rule.Id, Label(fact.Location!.Path, fact.Location.StartLine), violation.Message))))
            .Where(item => item.Item2 is not null && item.Item2 != "implicit-operator")
            .Select(item => Key(item.Id, item.Item2!, item.Message)).ToHashSet(StringComparer.Ordinal);
        var compilerCases = compiler.Select(item => (item.Id, Label(item.Location.SourceTree!.FilePath,
                item.Location.GetLineSpan().StartLinePosition.Line + 1), Message: item.GetMessage()))
            .Where(item => item.Item2 is not null && item.Item2 != "implicit-operator")
            .Select(item => Key(item.Id, item.Item2!, item.Message)).ToHashSet(StringComparer.Ordinal);
        var active = ForbiddenCases.Concat(configuration == "Debug" ? ["debug"] : Array.Empty<string>())
            .Concat(framework == "net10.0" ? ["net10"] : Array.Empty<string>());
        var expected = active.SelectMany(label => SharedRules.SelectMany(rule => ExpectedKeys(rule, label)))
            .Append(Key("AG005", "exception", "modules.Orders.forbiddenNamespaces restriction 'Legacy.Data'"))
            .ToHashSet(StringComparer.Ordinal);
        EqualSets(expected, solutionCases);
        EqualSets(expected, compilerCases);
        EqualSets(solutionCases, compilerCases);

        // Implicit operator binding belongs to solution extraction; explicit parameter types above are shared.
        EqualSets(SharedRules, results.Where(result => result.Violations.Any(violation => violation.Evidence.Any(fact =>
                fact.Location is { } location && Label(location.Path, location.StartLine) == "implicit-operator")))
            .Select(result => result.Violations[0].Rule.Id));
        Assert.DoesNotContain(compiler, item => Label(item.Location.SourceTree!.FilePath,
            item.Location.GetLineSpan().StartLinePosition.Line + 1) == "implicit-operator");
        Assert.All(results.SelectMany(result => result.Violations).SelectMany(item => item.Evidence),
            fact => { if (fact.Location is { } location) { Assert.False(Path.IsPathFullyQualified(location.Path)); } });
        // This project has a forbidden reference but no semantic use of its target.
        Assert.All(results.Take(2), result =>
        {
            var unused = Assert.Single(result.Violations, violation => violation.Message.Contains("Orders.Unused/Orders.Unused.csproj", StringComparison.Ordinal));
            Assert.All(unused.Evidence, fact => Assert.Null(fact.Location));
            Assert.Null(unused.Location);
        });
        Assert.DoesNotContain(results.Skip(2).SelectMany(result => result.Violations),
            violation => violation.Message.Contains("Orders.Unused/Orders.Unused.csproj", StringComparison.Ordinal));
        Assert.DoesNotContain(compiler, diagnostic => Path.GetFileName(diagnostic.Location.SourceTree!.FilePath) == "Unused.cs");

        string? Label(string path, int line) => labels.GetValueOrDefault((Path.GetFileName(path), line));
    }

    /// <summary>A multi-target input without selection cannot pass any shared rule's solution gate.</summary>
    [Fact]
    public async Task Unselected_framework_preserves_incomplete_coverage()
    {
        var root = Path.Combine(fixture.Root, "SemanticParity");
        using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(root, "Orders/Orders.csproj"), isTrusted: true));
        Assert.Equal(AnalysisStatus.Incomplete, workspace.Status);
        Assert.Contains(workspace.Issues, issue => issue.Code == "framework-required");
        var policy = await new YamlPolicyLoader().LoadAsync(Path.Combine(root, "architecture.guardian.yaml"));
        var context = await new DependencyGraphBuilder().BuildAsync(workspace, root, policy);
        var result = await new AnalysisService<DependencyBuildResult>(Rules()).AnalyzeAsync(new(context));
        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.False(policy.Gate.Passes(result));
    }

    /// <summary>Compiler errors remain host errors; unresolved names cannot establish clean solution coverage.</summary>
    [Fact]
    public async Task Missing_references_preserve_partial_findings_and_reject_a_clean_gate()
    {
        using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(fixture.Root,
            "PartialReference/PartialReference.csproj"), isTrusted: true));
        Assert.Equal(AnalysisStatus.Incomplete, workspace.Status);
        var policy = FrameworkPolicy();
        var context = await new DependencyGraphBuilder().BuildAsync(workspace, fixture.Root, policy);
        var result = await new AnalysisService<DependencyBuildResult>(Rules()).AnalyzeAsync(new(context));
        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.NotEmpty(result.Issues);
        Assert.False(policy.Gate.Passes(result));
        EqualSets(["AG005", "AG006"], result.Diagnostics.Select(item => item.RuleId));
        Assert.DoesNotContain(result.Diagnostics, item => item.Target.Symbol?.Contains("Unavailable", StringComparison.Ordinal) == true);
        var compilation = (await Assert.Single(workspace.Solution!.Projects).GetCompilationAsync())!;
        Assert.Contains(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var compiler = await AnalyzeAsync(workspace.Solution!, FrameworkSnapshot("PartialReference"));
        EqualSets(["AG005", "AG006"], compiler.Select(item => item.Id));
        Assert.DoesNotContain(compiler, item => item.GetMessage().Contains("Unavailable", StringComparison.Ordinal));
    }

    /// <summary>Actual source-generator trees participate in both engines with portable solution evidence.</summary>
    [Fact]
    public async Task Source_generator_output_is_checked_in_both_engines()
    {
        using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(fixture.Root,
            "GeneratorConsumer/GeneratorConsumer.csproj"), isTrusted: true));
        Complete(workspace.Status, workspace.Issues);
        var context = await new DependencyGraphBuilder().BuildAsync(workspace, fixture.Root, FrameworkPolicy());
        Complete(context.Status, context.Issues);
        var results = await EvaluateAsync(context);
        var compiler = await AnalyzeAsync(workspace.Solution!, FrameworkSnapshot("GeneratorConsumer"));
        EqualSets(["AG005", "AG006"], results.Where(result => result.Violations.Count > 0).Select(result => result.Violations[0].Rule.Id));
        EqualSets(["AG005", "AG006"], compiler.Select(item => item.Id));
        Assert.All(results.SelectMany(result => result.Violations), violation =>
        {
            Assert.Contains(violation.Target.Symbol, new[] { "System.Text.StringBuilder", "System.Text.StringBuilder.StringBuilder()" });
            Assert.All(violation.Evidence, fact => Assert.StartsWith("generated/", fact.Location!.Path));
        });
        Assert.All(compiler, diagnostic => Assert.EndsWith(".g.cs", diagnostic.Location.SourceTree!.FilePath));
    }

    private static IArchitectureRule<DependencyBuildResult>[] Rules() => [new ModuleDependenciesRule(), new LayerDependenciesRule(),
        new ImplementationBoundariesRule(), new ForbiddenReferencesRule(), new DomainInfrastructureRule()];

    private static async Task<RuleEvaluationResult[]> EvaluateAsync(DependencyBuildResult context)
    {
        var results = await Task.WhenAll(Rules().Select(rule => rule.EvaluateAsync(context)));
        Assert.All(results, result => Complete(result.Status, result.Issues));
        return results;
    }

    private static async Task<Microsoft.CodeAnalysis.Diagnostic[]> AnalyzeAsync(Solution solution, string snapshot)
    {
        var diagnostics = new List<Microsoft.CodeAnalysis.Diagnostic>();
        foreach (var project in solution.Projects)
        {
            var compilation = (await project.GetCompilationAsync())!;
            var driver = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DependencyAnalyzer()),
                new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(new Snapshot(snapshot))));
            diagnostics.AddRange(await driver.GetAnalyzerDiagnosticsAsync());
        }
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Contains(diagnostic.Id, SharedRules);
            Assert.True(diagnostic.Location.IsInSource);
        });
        return diagnostics.ToArray();
    }

    private static ArchitecturePolicy FrameworkPolicy() => new([new("All", ["**/*.csproj"], forbiddenNamespaces: ["System.Text"])],
        [new("All", ["**/*.csproj"], domainInfrastructure: new(forbiddenNamespaces: ["System.Text"]))],
        SharedRules.ToDictionary(id => id, _ => Severity.Error));

    private static string FrameworkSnapshot(string assembly) => "<ArchitectureGuardian version='1'>"
        + "<Module id='All' forbiddenNamespaces='System.Text'/><Layer id='All' infrastructureNamespaces='System.Text'/>"
        + $"<Assembly name='{assembly}' module='All' layer='All'/></ArchitectureGuardian>";

    private static Dictionary<(string File, int Line), string> Labels(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "Orders"), "*.cs").SelectMany(path => File.ReadAllLines(path)
            .Select((line, index) => (File: Path.GetFileName(path), Line: index + 1, Label: line.Split("// case:").ElementAtOrDefault(1))))
            .Where(item => item.Label is not null).ToDictionary(item => (item.File, item.Line), item => item.Label!.Trim());

    private static IEnumerable<string> ExpectedKeys(string rule, string label) => rule switch
    {
        "AG005" => [Key(rule, label, "modules.Orders.forbiddenNamespaces restriction 'Legacy.Data'"),
            Key(rule, label, "layers.Domain.forbiddenAssemblies restriction 'Billing.Persistence'")],
        "AG006" => [Key(rule, label, "layers.Domain.domainInfrastructure.forbiddenNamespaces restriction 'Legacy.Data'")],
        _ => [Key(rule, label, "")],
    };

    private static string Key(string rule, string label, string message)
    {
        var restriction = message.Contains(", violating ", StringComparison.Ordinal)
            ? message.Split(", violating ")[1].TrimEnd('.') : message.Contains(" restriction '", StringComparison.Ordinal) ? message : "";
        return $"{rule}|{label}|{restriction}";
    }

    private static void EqualSets(IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.Equal(expected.Distinct().Order(StringComparer.Ordinal), actual.Distinct().Order(StringComparer.Ordinal));

    private static void Complete(AnalysisStatus status, IReadOnlyList<AnalysisIssue> issues) =>
        Assert.True(status == AnalysisStatus.Completed, string.Join(Environment.NewLine, issues.Select(issue => issue.Code + ": " + issue.Message)));

    private sealed class Snapshot(string text) : AdditionalText
    {
        public override string Path => "architecture.guardian.compiler.xml";
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }
}
