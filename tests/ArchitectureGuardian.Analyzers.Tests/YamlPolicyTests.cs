using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ArchitectureGuardian.Analyzers.Tests;

/// <summary>Checks YAML selection, defaults and host ownership independently of disk/project evaluation.</summary>
public sealed class YamlPolicyTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "archguard-yaml-tests");
    private const string Yaml = """
        version: 1
        modules:
          - id: Orders
            projects: [Orders/*.csproj]
            forbiddenNamespaces: [Billing]
          - id: Billing
            projects: [Billing/*.csproj]
        layers:
          - id: Application
            projects: [Orders/*.csproj]
            domainInfrastructure:
              forbiddenNamespaces: [Billing]
          - id: Infrastructure
            projects: [Billing/*.csproj]
        rules:
          AG001: error
          AG002: error
          AG003: error
          AG004: error
          AG005: error
          AG006: error
        """;

    /// <summary>Maps custom assembly names from both DLL and in-memory project references.</summary>
    /// <param name="inMemory">Whether the host binds a source compilation.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_yaml_checks_all_supported_rules_with_custom_assembly_names(bool inMemory)
    {
        var diagnostics = await Analyze(Yaml, inMemory);
        Assert.Equal(new[] { "AG001", "AG002", "AG004", "AG005", "AG006" }, diagnostics.Select(item => item.Id).Order());
        Assert.All(diagnostics, item => Assert.Equal(DiagnosticSeverity.Error, item.Severity));
    }

    /// <summary>Only selected IDs run and YAML controls their default severity.</summary>
    /// <param name="severity">The YAML severity.</param>
    /// <param name="expected">The Roslyn reporting level.</param>
    [Theory]
    [InlineData("info", DiagnosticSeverity.Info)]
    [InlineData("warning", DiagnosticSeverity.Warning)]
    [InlineData("error", DiagnosticSeverity.Error)]
    public async Task Yaml_selection_and_severity_are_applied(string severity, DiagnosticSeverity expected)
    {
        var yaml = Yaml.Substring(0, Yaml.IndexOf("rules:", StringComparison.Ordinal)) + "rules:\n  AG001: " + severity;
        var diagnostic = Assert.Single(await Analyze(yaml));
        Assert.Equal("AG001", diagnostic.Id);
        Assert.Equal(expected, diagnostic.Severity);
    }

    /// <summary>Standard compilation options can downgrade or suppress YAML defaults.</summary>
    /// <param name="setting">The host override.</param>
    [Theory]
    [InlineData(ReportDiagnostic.Warn)]
    [InlineData(ReportDiagnostic.Suppress)]
    public async Task Host_overrides_yaml_defaults(ReportDiagnostic setting)
    {
        var yaml = Yaml.Substring(0, Yaml.IndexOf("rules:", StringComparison.Ordinal)) + "rules:\n  AG001: error";
        var diagnostics = await Analyze(yaml, setting: setting);
        if (setting == ReportDiagnostic.Suppress) { Assert.Empty(diagnostics); }
        else { Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(diagnostics).Severity); }
    }

    /// <summary>Malformed policies produce a located operational diagnostic, including under concurrent loading.</summary>
    /// <param name="yaml">The invalid document.</param>
    [Theory]
    [InlineData("version: 2")]
    [InlineData("version: [")]
    [InlineData("version: 1\nunknown: value")]
    public async Task Invalid_yaml_has_a_document_location(string yaml)
    {
        var diagnostic = Assert.Single(await Analyze(yaml));
        Assert.Equal("AG000", diagnostic.Id);
        Assert.EndsWith("architecture.guardian.yaml", diagnostic.Location.GetLineSpan().Path);
    }

    /// <summary>Missing build context and ambiguous ownership never produce clean analysis.</summary>
    [Fact]
    public async Task Missing_or_ambiguous_ownership_reports_AG000()
    {
        Assert.Equal("AG000", Assert.Single(await Analyze(Yaml, missingContext: true)).Id);
        Assert.Equal("AG000", Assert.Single(await Analyze(Yaml.Replace("Billing/*.csproj", "Orders/*.csproj", StringComparison.Ordinal))).Id);
        Assert.Equal("AG000", Assert.Single(await Analyze(Yaml, missingReferenceIdentity: true)).Id);
    }

    /// <summary>An in-memory project establishes required ownership even when its metadata input is absent.</summary>
    [Fact]
    public async Task Missing_in_memory_reference_metadata_never_produces_clean_analysis()
    {
        var diagnostic = Assert.Single(await Analyze(Yaml, omitReferenceMetadata: true));
        Assert.Equal("AG000", diagnostic.Id);
        Assert.Contains("Custom.Billing", diagnostic.GetMessage());
        Assert.Contains("ownership metadata", diagnostic.GetMessage());
    }

    /// <summary>Explicit mixed-format and duplicate configuration is rejected instead of selecting a fallback.</summary>
    [Fact]
    public async Task Mixed_and_duplicate_policies_are_rejected()
    {
        Assert.Equal("AG000", Assert.Single(await Analyze(Yaml, extraPolicy: "architecture.guardian.compiler.xml")).Id);
        Assert.Equal("AG000", Assert.Single(await Analyze(Yaml, extraPolicy: "architecture.guardian.yaml")).Id);
    }

    /// <summary>Compilation-local policy state is isolated during concurrent reuse of the same analyzer.</summary>
    [Fact]
    public async Task Concurrent_yaml_compilations_remain_independent()
    {
        var selected = Yaml.Substring(0, Yaml.IndexOf("rules:", StringComparison.Ordinal)) + "rules:\n  AG003: error";
        var results = await Task.WhenAll(Analyze(Yaml), Analyze(selected));
        Assert.Equal(5, results[0].Length);
        Assert.Empty(results[1]);
    }

    /// <summary>YAML selection and severities apply to return, parameter and nested function-pointer aliases.</summary>
    /// <param name="signature">The alias target.</param>
    [Theory]
    [InlineData("delegate*<Billing.Service, void>")]
    [InlineData("delegate*<Billing.Service>")]
    [InlineData("delegate*<Billing.Service[], void>")]
    [InlineData("delegate*<Billing.Service*, void>")]
    [InlineData("delegate*<System.Collections.Generic.List<Billing.Service[]>, void>")]
    [InlineData("delegate*<delegate*<Billing.Service, void>, void>")]
    [InlineData("delegate*<Billing.Service, Billing.Service, Billing.Service>")]
    public async Task Function_pointer_aliases_use_yaml_rules_and_alias_locations(string signature)
    {
        var source = $"using unsafe Callback = {signature}; public unsafe class Order {{ public Callback Value; }}";
        var diagnostics = await Analyze(Yaml, source: source);
        Assert.Equal(new[] { "AG001", "AG002", "AG004", "AG005", "AG006" }, diagnostics.Select(item => item.Id).Order());
        Assert.All(diagnostics, item =>
        {
            Assert.Equal(DiagnosticSeverity.Error, item.Severity);
            Assert.Equal(source.LastIndexOf("Callback", StringComparison.Ordinal), item.Location.SourceSpan.Start);
            Assert.Equal("Callback".Length, item.Location.SourceSpan.Length);
        });
        Assert.Empty(await Analyze(Yaml, source: $"using unsafe Callback = {signature}; public class Order {{ }}"));
    }

    private static async Task<Diagnostic[]> Analyze(string yaml, bool inMemory = true, ReportDiagnostic? setting = null,
        bool missingContext = false, bool missingReferenceIdentity = false, string? extraPolicy = null, bool omitReferenceMetadata = false,
        string source = "public class Order : Billing.Service { }")
    {
        var references = new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
        var billing = CSharpCompilation.Create("Custom.Billing", [CSharpSyntaxTree.ParseText("namespace Billing { public class Service { } }")],
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        MetadataReference reference = billing.ToMetadataReference();
        if (!inMemory)
        {
            using var stream = new MemoryStream();
            Assert.True(billing.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success);
            reference = MetadataReference.CreateFromImage(stream.ToArray(), filePath: Path.Combine(Root, "Custom.Billing.dll"));
        }
        var compilation = CSharpCompilation.Create("Custom.Orders", [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), path: Path.Combine(Root, "Order.cs"))],
            references.Append(reference), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                specificDiagnosticOptions: setting is null ? null : ImmutableDictionary<string, ReportDiagnostic>.Empty.Add("AG001", setting.Value)));
        Assert.DoesNotContain(compilation.GetDiagnostics(TestContext.Current.CancellationToken), item => item.Severity == DiagnosticSeverity.Error);
        var policy = new TextFile(Path.Combine(Root, "architecture.guardian.yaml"), yaml);
        var project = new TextFile(Path.Combine(Root, "Billing", "UnrelatedFilename.csproj"), null);
        var files = omitReferenceMetadata ? ImmutableArray.Create<AdditionalText>(policy) : ImmutableArray.Create<AdditionalText>(policy, project);
        if (extraPolicy is not null) { files = files.Add(new TextFile(Path.Combine(Root, "extra", extraPolicy), "invalid")); }
        var options = new Options(missingContext ? new Dictionary<string, string>() : new()
        {
            ["build_property.MSBuildProjectFullPath"] = Path.Combine(Root, "Orders", "AlsoUnrelated.csproj"),
        });
        var metadata = new Options(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.ArchitectureGuardianReferencePath"] = Path.Combine(Root, "Custom.Billing.dll"),
            ["build_metadata.AdditionalFiles.ArchitectureGuardianAssemblyName"] = missingReferenceIdentity ? "" : "Custom.Billing",
        });
        var driver = compilation.WithAnalyzers([new DependencyAnalyzer()], new AnalyzerOptions(files, new Provider(options, project, metadata)));
        return (await driver.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken)).ToArray();
    }

    private sealed class TextFile(string path, string? text) : AdditionalText
    {
        public override string Path => path;
        public override SourceText? GetText(CancellationToken cancellationToken = default) => text is null ? null : SourceText.From(text);
    }

    private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }

    private sealed class Provider(Options globals, AdditionalText project, Options metadata) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions => globals;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options([]);
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => textFile == project ? metadata : new Options([]);
    }
}
