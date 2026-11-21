using ArchitectureGuardian.Analysis.Abp;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using DiagnosticSeverity = ArchitectureGuardian.Core.Diagnostics.DiagnosticSeverity;
using ArchitectureGuardian.Core.Discovery;
using ArchitectureGuardian.Core.Frameworks.Abp;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Repositories;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ArchitectureGuardian.Tests.Abp;

/// <summary>Verifies ABP rule and extractor edge cases that the restored fixture cannot express.</summary>
public sealed class AbpUnitTests
{
    private const string A = "src/A/A.csproj";
    private const string B = "src/B/B.csproj";
    private const string C = "src/C/C.csproj";

    /// <summary>ABP001 keeps a reference whose other path runs through a project without a module, so the gap is still reported.</summary>
    [Fact]
    public async Task Module_gaps_behind_projects_without_modules_are_reported()
    {
        // A references C directly and through helper B, which has no module; nothing else would check A → C.
        var violations = await EvaluateAsync([new(A, "A.AModule", []), new(C, "C.CModule", [])], [new(A, B), new(B, C), new(A, C)]);
        var violation = Assert.Single(violations);
        Assert.Equal(["C.CModule"], Assert.Single(violation.Context!.PolicyConstraints).Values);
        Assert.Contains("references project 'src/C/C.csproj'", violation.Message, StringComparison.Ordinal);
    }

    /// <summary>ABP001 leaves a reference to the intermediate module project, which reports its own gap once.</summary>
    [Fact]
    public async Task Module_gaps_are_reported_by_the_closest_module()
    {
        // B is a module that references C without declaring it; A declares B, so only B → C is a gap.
        var violations = await EvaluateAsync([new(A, "A.AModule", ["B.BModule"]), new(B, "B.BModule", []), new(C, "C.CModule", [])],
            [new(A, B), new(B, C), new(A, C)]);
        var violation = Assert.Single(violations);
        Assert.Equal("B.BModule", violation.Target.Symbol);
    }

    /// <summary>A referenced assembly that copies ABP names is not the framework, so it yields no ABP facts.</summary>
    /// <param name="assembly">The name of the referenced assembly that declares the ABP-named types.</param>
    /// <param name="recognized">Whether the extractor should treat the assembly as ABP.</param>
    [Theory]
    [InlineData("Shim", false)]
    [InlineData("Volo.Abp.Core", true)]
    public async Task Framework_types_must_come_from_framework_assemblies(string assembly, bool recognized)
    {
        var shim = CSharpCompilation.Create(assembly, [CSharpSyntaxTree.ParseText("""
            namespace Volo.Abp.Modularity { public abstract class AbpModule { } }
            namespace Volo.Abp.Application.Services { public interface IApplicationService { } }
            """, cancellationToken: TestContext.Current.CancellationToken)], Platform(), new(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        Assert.True(shim.Emit(image, cancellationToken: TestContext.Current.CancellationToken).Success);

        var root = Path.Combine(Path.GetTempPath(), "arch-guard-abp-" + Guid.NewGuid().ToString("N"));
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "App", "App", LanguageNames.CSharp,
            filePath: Path.Combine(root, "App", "App.csproj"), compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: [.. Platform(), MetadataReference.CreateFromImage(image.ToArray())]));
        workspace.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(project.Id), "App.cs", filePath: Path.Combine(root, "App", "App.cs"),
            loader: TextLoader.From(TextAndVersion.Create(Microsoft.CodeAnalysis.Text.SourceText.From("""
                public class AppModule : Volo.Abp.Modularity.AbpModule { }
                public class OrderAppService : Volo.Abp.Application.Services.IApplicationService { public void Run() { } }
                """), VersionStamp.Create()))));

        var (facts, issues) = await AbpFactExtractor.ExtractAsync(workspace.CurrentSolution,
            [new RepositoryProject("App/App.csproj", "App", new("App/App.csproj", "App", []))], root, TestContext.Current.CancellationToken);
        Assert.Empty(issues);
        Assert.Equal(recognized, facts.Modules.Count == 1);
        Assert.Equal(recognized, facts.ServiceMethods.Count == 1);
    }

    /// <summary>The shared discovery template cannot be changed by callers through its read-only views.</summary>
    [Fact]
    public void Discovery_template_is_deeply_read_only()
    {
        Assert.True(((ICollection<KeyValuePair<string, DiagnosticSeverity>>)AbpSolutionTemplate.Rules).IsReadOnly);
        Assert.All(AbpSolutionTemplate.Layers, layer =>
        {
            Assert.True(((ICollection<string>)layer.AllowedDependencies).IsReadOnly);
            Assert.True(((ICollection<string>)layer.ForbiddenNamespaces).IsReadOnly);
        });
        Assert.True(((ICollection<string>)AbpSolutionTemplate.RepositoryLayers).IsReadOnly);
    }

    private static IEnumerable<MetadataReference> Platform() => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Where(path => Path.GetFileName(path) is "System.Private.CoreLib.dll" or "System.Runtime.dll" or "netstandard.dll")
        .Select(path => MetadataReference.CreateFromFile(path));

    private static async Task<IReadOnlyList<Core.Rules.RuleViolation>> EvaluateAsync(IEnumerable<AbpModuleClass> modules, IEnumerable<AbpProjectReference> references)
    {
        var policy = new ArchitecturePolicy([new("App", ["src/**/*.csproj"])], [new("All", ["src/**/*.csproj"])],
            new Dictionary<string, DiagnosticSeverity> { ["ABP001"] = DiagnosticSeverity.Error });
        var repository = new RepositoryModel("App.slnx", policy, new[] { A, B, C }.Select(path => new RepositoryProject(path, path, new(path, path, []))));
        var context = new DependencyBuildResult(repository, new DependencyGraph([], []), AnalysisStatus.Completed,
            abp: new AbpFacts(modules, references, [], [], [], []));
        var result = await new ModuleDependencyDeclarationsRule().EvaluateAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisStatus.Completed, result.Status);
        return result.Violations;
    }
}
