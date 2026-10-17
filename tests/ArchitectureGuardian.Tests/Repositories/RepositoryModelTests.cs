using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Repositories;
using Xunit;

namespace ArchitectureGuardian.Tests.Repositories;

/// <summary>Checks portable model identities, immutable ownership and strict configuration resolution.</summary>
public sealed class RepositoryModelTests
{
    /// <summary>Scopes generated file-local types by generator and hint without accepting a physical output directory.</summary>
    [Fact]
    public void Generated_file_discriminators_are_logical_and_distinct_from_authored_paths()
    {
        var generated = new RepositoryGeneratedFile("Generators", "Company.Generator", "Helpers/Local.g.cs");
        var normalized = new RepositoryGeneratedFile("Generators", "Company.Generator", "Helpers\\Local.g.cs");
        var first = new RepositoryType("src/A.csproj", "A", "A.Helper", RepositoryTypeKind.Class, false, generatedFile: generated);
        var otherHint = new RepositoryType("src/A.csproj", "A", "A.Helper", RepositoryTypeKind.Class, false,
            generatedFile: new("Generators", "Company.Generator", "Other.g.cs"));
        var otherGenerator = new RepositoryType("src/A.csproj", "A", "A.Helper", RepositoryTypeKind.Class, false,
            generatedFile: new("Generators", "Company.OtherGenerator", generated.HintName));
        var otherAssembly = new RepositoryType("src/A.csproj", "A", "A.Helper", RepositoryTypeKind.Class, false,
            generatedFile: new("OtherGenerators", "Company.Generator", generated.HintName));

        Assert.Equal(generated.Id, normalized.Id);
        Assert.Equal(4, new[] { first.Id, otherHint.Id, otherGenerator.Id, otherAssembly.Id }.Distinct().Count());
        Assert.Null(first.DeclaringFile);
        Assert.Same(generated, first.GeneratedFile);
        Assert.Throws<ArgumentException>(() => new RepositoryType("src/A.csproj", "A", "A.Helper", RepositoryTypeKind.Class, false,
            "Local.cs", generated));
    }

    /// <summary>Snapshots every collection and keeps similarly named assemblies and declarations in distinct projects.</summary>
    [Fact]
    public void Identities_are_scoped_and_collections_are_immutable()
    {
        var types = new List<RepositoryType> { new("src/A.csproj", "Shared", "Shared.Type`1", RepositoryTypeKind.Class, true) };
        var namespaces = new List<RepositoryNamespace> { new("src/A.csproj", "Shared", types) };
        var assembly = new RepositoryAssembly("src/A.csproj", "Shared", namespaces);
        var projects = new List<RepositoryProject> { new("src/A.csproj", "Display", assembly) };
        var model = new RepositoryModel("App.slnx", Policy(), projects);
        types.Clear();
        namespaces.Clear();
        projects.Clear();

        var project = Assert.Single(model.Projects);
        var ns = Assert.Single(project.Assembly.Namespaces);
        var type = Assert.Single(ns.Types);
        Assert.Equal("src/A.csproj", project.Id);
        Assert.Equal(type.Id, new RepositoryType("src\\A.csproj", "Shared", "Shared.Type`1", RepositoryTypeKind.Class, false).Id);
        Assert.NotEqual(type.Id, new RepositoryType("src/B.csproj", "Shared", "Shared.Type`1", RepositoryTypeKind.Class, true).Id);
        Assert.NotEqual(assembly.Id, new RepositoryAssembly("src/B.csproj", "Shared", []).Id);
        Assert.NotEqual(ns.Id, new RepositoryNamespace("src/B.csproj", "Shared", []).Id);
        Assert.Throws<NotSupportedException>(() => ((IList<RepositoryProject>)model.Projects).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<RepositoryModule>)model.Modules).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<RepositoryLayer>)model.Layers).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<RepositoryNamespace>)assembly.Namespaces).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<RepositoryType>)ns.Types).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<RepositoryProject>)model.Modules[0].Projects).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<RepositoryProject>)model.Layers[0].Projects).Clear());
    }

    /// <summary>Rejects absent/ambiguous owners, unused selectors and duplicate inventory rather than weakening policy.</summary>
    [Fact]
    public void Model_resolution_uses_the_strict_policy_contract()
    {
        var project = Project("src/A.csproj");
        Assert.Throws<ArgumentException>(() => new RepositoryModel("App.slnx", Policy(), []));
        Assert.Throws<ArgumentException>(() => new RepositoryModel("App.slnx", Policy(), [project, project]));
        Assert.Throws<ArgumentException>(() => new RepositoryModel("App.slnx", Policy(), [project, Project("extra.csproj")]));
        var rules = new Dictionary<string, DiagnosticSeverity> { ["AG001"] = DiagnosticSeverity.Error };
        var modules = new[] { new ModulePolicy("App", ["src/*.csproj"]) };
        var layers = new[] { new LayerPolicy("All", ["src/*.csproj"]) };
        Assert.Contains("exactly one", Assert.Throws<ArgumentException>(() => new RepositoryModel("App.slnx",
            new(modules.Append(new("Other", ["src/*.csproj"])), layers, rules), [project])).Message);
        Assert.Contains("exactly one", Assert.Throws<ArgumentException>(() => new RepositoryModel("App.slnx",
            new(modules, layers.Append(new("Other", ["src/*.csproj"])), rules), [project])).Message);
        Assert.Contains("multiple public contracts", Assert.Throws<ArgumentException>(() => new RepositoryModel("App.slnx",
            new([new("App", ["src/*.csproj"], [new("api", ["src/*.csproj"]), new("other", ["src/*.csproj"])])], layers, rules), [project])).Message);
        Assert.Contains("matches no project", Assert.Throws<ArgumentException>(() => new RepositoryModel("App.slnx",
            new([new("App", ["src/*.csproj", "missing.csproj"])], layers, rules), [project])).Message);
    }

    /// <summary>Rejects inconsistent declaration ownership and repeated scoped identities.</summary>
    [Fact]
    public void Declaration_scopes_are_validated()
    {
        var type = new RepositoryType("src/A.csproj", "A", "A.Type", RepositoryTypeKind.Class, true);
        var ns = new RepositoryNamespace("src/A.csproj", "A", [type]);
        Assert.Throws<ArgumentException>(() => new RepositoryNamespace("src/B.csproj", "A", [type]));
        Assert.Throws<ArgumentException>(() => new RepositoryNamespace("src/A.csproj", "B", [type]));
        Assert.Throws<ArgumentException>(() => new RepositoryNamespace("src/A.csproj", "A", [type, type]));
        Assert.Throws<ArgumentException>(() => new RepositoryAssembly("src/B.csproj", "A", [ns]));
        Assert.Throws<ArgumentException>(() => new RepositoryProject("src/B.csproj", "B", new("src/A.csproj", "A", [])));
        Assert.Throws<ArgumentException>(() => new RepositoryProject("/absolute/A.csproj", "A", new("src/A.csproj", "A", [])));
    }

    /// <summary>Completion requires a model and no operational issues; cancellation never needs a placeholder model.</summary>
    [Fact]
    public void Build_results_preserve_coverage_invariants()
    {
        Assert.Throws<ArgumentException>(() => new RepositoryBuildResult(null, AnalysisStatus.Completed));
        Assert.Throws<ArgumentException>(() => new RepositoryBuildResult(null, AnalysisStatus.Incomplete));
        var model = new RepositoryModel("App.slnx", Policy(), [Project("src/A.csproj")]);
        Assert.Throws<ArgumentException>(() => new RepositoryBuildResult(model, AnalysisStatus.Completed, [new("load", "Missing context.")]));
        Assert.Equal(AnalysisStatus.Cancelled, new RepositoryBuildResult(null, AnalysisStatus.Cancelled).Status);
    }

    private static RepositoryProject Project(string path) => new(path, "Display", new(path, "Assembly", []));

    private static ArchitecturePolicy Policy() => new([new("App", ["src/*.csproj"])], [new("All", ["src/*.csproj"])],
        new Dictionary<string, DiagnosticSeverity> { ["AG001"] = DiagnosticSeverity.Error });
}
