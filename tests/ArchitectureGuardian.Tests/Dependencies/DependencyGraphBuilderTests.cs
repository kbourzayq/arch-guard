using System.Xml.Linq;
using ArchitectureGuardian.Analysis.Dependencies;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Rules;
using ArchitectureGuardian.Tests.Workspaces;
using Xunit;

namespace ArchitectureGuardian.Tests.Dependencies;

/// <summary>Verifies semantic extraction and evidence against real, isolated MSBuild workspaces.</summary>
/// <param name="fixture">Restored workspace inputs.</param>
[Trait("Category", "Integration")]
public sealed class DependencyGraphBuilderTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private const string Orders = "src/Orders/Orders.Application/Orders.Application.csproj";
    private const string Contracts = "src/Billing/Billing.Contracts/Billing.Contracts.csproj";
    private const string Infrastructure = "src/Billing/Billing.Infrastructure/Billing.Infrastructure.csproj";

    /// <summary>Connects all graph levels and retains member and source evidence through logical aggregation.</summary>
    [Fact]
    public async Task Project_and_semantic_dependencies_keep_evidence_at_every_level()
    {
        var result = await BuildAsync();
        Complete(result);
        var graph = result.Graph!;
        var project = Assert.Single(graph.Edges, edge => edge.Kind == DependencyKind.Project
            && edge.Source.Identity == Orders && edge.Target.Identity == Contracts);
        Assert.Equal(Contracts, Assert.Single(project.Evidence).ProjectReference);
        Assert.Contains(graph.Edges, edge => edge.Kind == DependencyKind.Assembly
            && edge.Source.ProjectPath == Orders && edge.Target.ProjectPath == Contracts);
        var type = Assert.Single(graph.Edges, edge => edge.Kind == DependencyKind.Type
            && edge.Source.Name == "Orders.Application.SubmitOrder" && edge.Target.Name == "Billing.Contracts.IInvoice");
        var use = type.Evidence.First(evidence => evidence.TargetSymbol == "Billing.Contracts.IInvoice.Total");
        Assert.Contains("SubmitOrder.Total", use.SourceSymbol);
        Assert.Equal("src/Orders/Orders.Application/SubmitOrder.cs", use.Fact.Location!.Path);
        Assert.Equal(5, use.Fact.Location.StartLine);
        Assert.Contains(graph.Edges, edge => edge.Kind == DependencyKind.Namespace
            && edge.Source.Name == "Orders.Application" && edge.Target.Name == "Billing.Contracts" && edge.Evidence.Contains(use));
        var module = Assert.Single(graph.Edges, edge => edge.Kind == DependencyKind.Module);
        Assert.Equal("Orders", module.Source.Name);
        Assert.Equal("Billing", module.Target.Name);
        Assert.Contains(use, module.Evidence);
        Assert.Contains(graph.Edges, edge => edge.Kind == DependencyKind.Layer
            && edge.Source.Name == "Application" && edge.Target.Name == "Contracts" && edge.Evidence.Contains(use));
        Assert.Contains(graph.Nodes, node => node.Kind == DependencyKind.Assembly && node.ProjectPath is null);
        Assert.Contains(graph.Edges, edge => edge.Kind == DependencyKind.Type && edge.Target.ProjectPath is null);
        Assert.All(graph.Edges.SelectMany(edge => edge.Evidence).Where(evidence => evidence.Fact.Location is not null),
            evidence => Assert.False(Path.IsPathFullyQualified(evidence.Fact.Location!.Path)));
        // BuildAsync has disposed the workspace: the graph and policy remain usable without host objects.
        Assert.True(result.Repository!.ResolvedPolicy.AllowsModuleDependency(Orders, Contracts));
        Assert.NotEmpty(graph.Traverse(type.Source.Id));
        var evaluation = await EvaluateDependenciesAsync(result, "AG001");
        Assert.True(evaluation.IsComplete);
        Assert.Empty(evaluation.Diagnostics);
        Assert.True(result.Repository.Policy.Gate.Passes(evaluation));
        var layers = await EvaluateDependenciesAsync(result, "AG002");
        Assert.True(layers.IsComplete);
        Assert.Empty(layers.Diagnostics);
        Assert.True(result.Repository.Policy.Gate.Passes(layers));
        var cycles = await EvaluateDependenciesAsync(result, "AG003");
        Assert.True(cycles.IsComplete);
        Assert.Empty(cycles.Diagnostics);
    }

    /// <summary>Enforces a seeded forbidden reference using the graph's project and semantic evidence.</summary>
    [Fact]
    public async Task Forbidden_dependency_can_be_traced_to_project_and_source_origins()
    {
        var root = Path.Combine(fixture.Root, "ModularMonolith");
        var projectPath = Path.Combine(root, Orders);
        var sourcePath = Path.Combine(root, "src/Orders/Orders.Application/Forbidden.cs");
        var targetPath = Path.Combine(root, "src/Billing/Billing.Infrastructure/VisibleStore.cs");
        var original = await File.ReadAllTextAsync(projectPath);
        try
        {
            var xml = XDocument.Parse(original);
            xml.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "../../Billing/Billing.Infrastructure/Billing.Infrastructure.csproj"))));
            await File.WriteAllTextAsync(projectPath, xml.ToString());
            await File.WriteAllTextAsync(sourcePath, "namespace Orders.Application; public class Forbidden { public Billing.Infrastructure.VisibleStore Create() => new(); }");
            await File.WriteAllTextAsync(targetPath, "namespace Billing.Infrastructure; public class VisibleStore;");
            await fixture.RestoreAsync(Path.Combine(root, "ModularMonolith.slnx"));
            var result = await BuildAsync();
            Complete(result);
            Assert.False(result.Repository!.ResolvedPolicy.AllowsModuleDependency(Orders, Infrastructure));
            Assert.False(result.Repository.ResolvedPolicy.AllowsLayerDependency(Orders, Infrastructure));
            var module = Assert.Single(result.Graph!.Edges, edge => edge.Kind == DependencyKind.Module);
            Assert.Contains(module.Evidence, evidence => evidence.SourceProject == Orders && evidence.TargetProject == Infrastructure
                && evidence.ProjectReference == Infrastructure);
            Assert.Contains(module.Evidence, evidence => evidence.SourceProject == Orders && evidence.TargetProject == Infrastructure
                && evidence.SourceSymbol?.Contains("Forbidden", StringComparison.Ordinal) == true && evidence.Fact.Location!.Path.EndsWith("Forbidden.cs", StringComparison.Ordinal));
            var evaluation = await EvaluateDependenciesAsync(result, "AG001");
            Assert.True(evaluation.IsComplete);
            // The SDK also exposes implementation references transitively to the compilation.
            // Every forbidden pair is reported, while contract facts stay out of those findings.
            Assert.All(evaluation.Diagnostics, item =>
            {
                Assert.Equal("Orders", item.Target.Module);
                Assert.Contains("module 'Billing'", item.Message);
                Assert.DoesNotContain(Contracts, item.Message);
            });
            var diagnostic = Assert.Single(evaluation.Diagnostics, item => item.Message.Contains(Infrastructure, StringComparison.Ordinal));
            Assert.Equal("AG001", diagnostic.RuleId);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal("Orders", diagnostic.Target.Module);
            Assert.Contains("module 'Billing'", diagnostic.Message);
            Assert.Contains(Infrastructure, diagnostic.Message);
            Assert.Contains(diagnostic.Evidence, fact => fact.Description.Contains(Infrastructure, StringComparison.Ordinal));
            Assert.Contains(diagnostic.Evidence, fact => fact.Location?.Path.EndsWith("Forbidden.cs", StringComparison.Ordinal) == true
                && fact.Description.Contains("VisibleStore", StringComparison.Ordinal));
            Assert.DoesNotContain(diagnostic.Evidence, fact => fact.Description.Contains("IInvoice", StringComparison.Ordinal));
            Assert.Equal("src/Orders/Orders.Application/Forbidden.cs", diagnostic.Location!.Path);
            Assert.False(result.Repository.Policy.Gate.Passes(evaluation));
            var layers = await EvaluateDependenciesAsync(result, "AG002");
            Assert.True(layers.IsComplete);
            var layerDiagnostic = Assert.Single(layers.Diagnostics);
            Assert.Equal("AG002", layerDiagnostic.RuleId);
            Assert.Contains("Layer 'Application'", layerDiagnostic.Message);
            Assert.Contains("layer 'Infrastructure'", layerDiagnostic.Message);
            Assert.Equal("src/Orders/Orders.Application/Forbidden.cs", layerDiagnostic.Location!.Path);
            Assert.Contains(layerDiagnostic.Evidence, fact => fact.Description.Contains(Infrastructure, StringComparison.Ordinal));
            Assert.Contains(layerDiagnostic.Evidence, fact => fact.Location?.Path.EndsWith("Forbidden.cs", StringComparison.Ordinal) == true
                && fact.Description.Contains("Forbidden.Create", StringComparison.Ordinal)
                && fact.Description.Contains("VisibleStore", StringComparison.Ordinal));
            Assert.DoesNotContain(layerDiagnostic.Evidence, fact => fact.Description.Contains("IInvoice", StringComparison.Ordinal));
            Assert.False(result.Repository.Policy.Gate.Passes(layers));
        }
        finally
        {
            await File.WriteAllTextAsync(projectPath, original);
            File.Delete(sourcePath);
            File.Delete(targetPath);
            // Restore the original asset graph as well as the project file for later fixture consumers.
            await fixture.RestoreAsync(Path.Combine(root, "ModularMonolith.slnx"));
        }
    }

    /// <summary>Includes generics, arrays, inheritance, constraints, attributes, aliases, extension calls and inferred types.</summary>
    [Fact]
    public async Task Semantic_forms_and_type_cycles_are_extracted()
    {
        var root = Path.Combine(fixture.Root, "ModularMonolith");
        var sourcePath = Path.Combine(root, "src/Billing/Billing.Application/Semantics.cs");
        var targetPath = Path.Combine(root, "src/Billing/Billing.Domain/Semantics.cs");
        try
        {
            await File.WriteAllTextAsync(targetPath, """
                namespace Billing.Domain;
                public class Base;
                public interface IMarker;
                public class Box<T>;
                public class MarkAttribute : System.Attribute;
                public static class Extensions { public static Base Convert(this string value) => new(); }
                public class Awaitable { public Awaiter GetAwaiter() => new(); }
                public class Awaiter : System.Runtime.CompilerServices.INotifyCompletion
                {
                    public bool IsCompleted => true;
                    public Base GetResult() => new();
                    public void OnCompleted(System.Action continuation) => continuation();
                }
                public class Sequence { public Enumerator GetEnumerator() => new(); }
                public class Enumerator
                {
                    public bool MoveNext() => false;
                    public Base Current => new();
                }
                """);
            await File.WriteAllTextAsync(sourcePath, """
                using Billing.Domain;
                using Alias = Billing.Domain.Box<Billing.Domain.Base>;
                namespace Billing.Application;
                [Mark]
                public class Semantics<T> : Base where T : IMarker
                {
                    public Alias[] Values = [];
                    public object Read() { var value = "input".Convert(); return value; }
                    public CycleA Cycle() => new();
                    public async System.Threading.Tasks.Task Iterate()
                    {
                        await new Awaitable();
                        foreach (var value in new Sequence()) { }
                    }
                }
                public class CycleA { public CycleB? Other; }
                public class CycleB { public CycleA? Other; }
                public class Outer { [Mark] public class Inner : Base; }
                file class LocalHelper { public Base? Value => null; }
                """);
            var result = await BuildAsync();
            Complete(result);
            var edges = result.Graph!.Edges.Where(edge => edge.Kind == DependencyKind.Type
                && edge.Source.Name == "Billing.Application.Semantics`1").ToArray();
            foreach (var target in new[] { "Base", "IMarker", "Box`1", "MarkAttribute", "Extensions", "Awaiter", "Enumerator" })
            {
                Assert.Contains(edges, edge => edge.Target.Name == "Billing.Domain." + target);
            }

            var cycle = Assert.Single(result.Graph.FindCycles(DependencyKind.Type), component => component.Any(node => node.Name == "Billing.Application.CycleA"));
            Assert.Equal(new[] { "Billing.Application.CycleA", "Billing.Application.CycleB" }, cycle.Select(node => node.Name).Order(StringComparer.Ordinal));
            Assert.Contains(result.Graph.Edges, edge => edge.Kind == DependencyKind.Layer && edge.Source.Name == "Application"
                && edge.Target.Name == "Domain" && edge.Evidence.Any(evidence => evidence.Fact.Location?.Path.EndsWith("Semantics.cs", StringComparison.Ordinal) == true));
            Assert.Contains(result.Graph.Edges, edge => edge.Kind == DependencyKind.Type
                && edge.Source.Name == "Billing.Application.LocalHelper" && edge.Target.Name == "Billing.Domain.Base");
            Assert.Contains(result.Graph.Edges, edge => edge.Kind == DependencyKind.Type
                && edge.Source.Name == "Billing.Application.Outer+Inner" && edge.Target.Name == "Billing.Domain.MarkAttribute");
            Assert.DoesNotContain(result.Graph.Edges, edge => edge.Kind == DependencyKind.Type
                && edge.Source.Name == "Billing.Application.Outer" && edge.Target.Name == "Billing.Domain.MarkAttribute");
        }
        finally
        {
            File.Delete(sourcePath);
            File.Delete(targetPath);
        }
    }

    /// <summary>Recognizes intrinsic nameof without discarding argument dependencies or real methods with that name.</summary>
    [Fact]
    public async Task Nameof_keeps_complete_coverage_and_argument_dependencies()
    {
        var root = Path.Combine(fixture.Root, "ModularMonolith");
        var sourcePath = Path.Combine(root, "src/Billing/Billing.Application/Names.cs");
        var callPath = Path.Combine(root, "src/Billing/Billing.Application/NamedCalls.cs");
        var targetPath = Path.Combine(root, "src/Billing/Billing.Domain/Names.cs");
        try
        {
            await File.WriteAllTextAsync(targetPath, """
                namespace Helpers;
                public class Referenced { public int Value => 0; }
                public static class NameFunctions { public static string @nameof() => "method"; }
                """);
            await File.WriteAllTextAsync(sourcePath, """
                namespace Billing.Application;
                public class NameConsumer
                {
                    public string M() => nameof(M);
                    public string Argument() => nameof(Helpers.Referenced.Value);
                }
                """);
            await File.WriteAllTextAsync(callPath, """
                using static Helpers.NameFunctions;
                namespace Billing.Application;
                public class NamedCallConsumer { public string OrdinaryCall() => @nameof(); }
                """);
            var result = await BuildAsync();
            Complete(result);
            var edges = result.Graph!.Edges.Where(edge => edge.Kind == DependencyKind.Type
                && edge.Source.Name == "Billing.Application.NameConsumer").ToArray();
            Assert.Contains(edges, edge => edge.Target.Name == "Helpers.Referenced"
                && edge.Evidence.Any(evidence => evidence.TargetSymbol == "Helpers.Referenced.Value"));
            Assert.Contains(result.Graph.Edges, edge => edge.Kind == DependencyKind.Type
                && edge.Source.Name == "Billing.Application.NamedCallConsumer" && edge.Target.Name == "Helpers.NameFunctions"
                && edge.Evidence.Any(evidence => evidence.TargetSymbol == "Helpers.NameFunctions.nameof()"));
        }
        finally
        {
            File.Delete(sourcePath);
            File.Delete(callPath);
            File.Delete(targetPath);
        }
    }

    /// <summary>Checks each deconstruction form's selected methods and source evidence in one shared compilation.</summary>
    [Fact]
    public async Task Deconstruction_preserves_compiler_selected_methods_and_evidence()
    {
        (string Statement, bool Nested)[] cases = [
            ("var (first, second) = \"x\";", false),
            ("int first, second; (first, second) = \"x\";", false),
            ("var (first, (second, third)) = \"x\";", true),
            ("foreach (var (first, second) in new[] { \"x\" }) { }", false),
            ("foreach (var (first, (second, third)) in new[] { \"x\" }) { }", true),
            ("var matched = \"x\" is (var first, var second);", false),
            ("var matched = \"x\" is (var first, (var second, var third));", true),
        ];
        var root = Path.Combine(fixture.Root, "ModularMonolith");
        var sourcePath = Path.Combine(root, "src/Billing/Billing.Application/Deconstruction.cs");
        var targetPath = Path.Combine(root, "src/Billing/Billing.Domain/Deconstruction.cs");
        try
        {
            await File.WriteAllTextAsync(targetPath, """
                namespace Helpers;
                public static class Extensions
                {
                    public static void Deconstruct(this string value, out int first, out int second) { first = 1; second = 2; }
                }
                public static class NestedExtensions
                {
                    public static void Deconstruct(this int value, out long first, out long second) { first = 1; second = 2; }
                }
                public static class UnusedExtensions
                {
                    public static void Deconstruct(this double value, out int first, out int second) { first = 1; second = 2; }
                }
                """);
            // Each case has its own method scope and source line, but shares one design-time build.
            var methods = cases.Select((item, index) => $"    public void Run{index}() {{ {item.Statement} }}");
            await File.WriteAllTextAsync(sourcePath, $$"""
                using Helpers;
                namespace Billing.Application;
                public class DeconstructionConsumer
                {
                {{string.Join("\n", methods)}}
                }
                """);
            var result = await BuildAsync();
            Complete(result);
            var graph = result.Graph!;
            for (var index = 0; index < cases.Length; index++)
            {
                var sourceSymbol = $"DeconstructionConsumer.Run{index}(";
                foreach (var target in cases[index].Nested ? new[] { "Helpers.Extensions", "Helpers.NestedExtensions" } : new[] { "Helpers.Extensions" })
                {
                    var edge = Assert.Single(graph.Edges, edge => edge.Kind == DependencyKind.Type
                        && edge.Source.Name == "Billing.Application.DeconstructionConsumer" && edge.Target.Name == target);
                    var evidence = edge.Evidence.First(evidence => evidence.TargetSymbol?.Contains(".Deconstruct(", StringComparison.Ordinal) == true
                        && evidence.SourceSymbol?.Contains(sourceSymbol, StringComparison.Ordinal) == true);
                    Assert.Equal("src/Billing/Billing.Application/Deconstruction.cs", evidence.Fact.Location!.Path);
                    Assert.Equal(5 + index, evidence.Fact.Location.StartLine);
                    Assert.Equal("src/Billing/Billing.Domain/Billing.Domain.csproj", evidence.TargetProject);
                    Assert.Contains(graph.Edges, edge => edge.Kind == DependencyKind.Namespace
                        && edge.Source.Name == "Billing.Application" && edge.Target.Name == "Helpers" && edge.Evidence.Contains(evidence));
                    Assert.Contains(graph.Edges, edge => edge.Kind == DependencyKind.Assembly && edge.Evidence.Contains(evidence));
                    Assert.Contains(graph.Edges, edge => edge.Kind == DependencyKind.Layer
                        && edge.Source.Name == "Application" && edge.Target.Name == "Domain" && edge.Evidence.Contains(evidence));
                }

                if (!cases[index].Nested)
                {
                    Assert.DoesNotContain(graph.Edges.Where(edge => edge.Kind == DependencyKind.Type && edge.Target.Name == "Helpers.NestedExtensions")
                        .SelectMany(edge => edge.Evidence), evidence => evidence.SourceSymbol?.Contains(sourceSymbol, StringComparison.Ordinal) == true);
                }
            }

            Assert.DoesNotContain(graph.Edges, edge => edge.Kind == DependencyKind.Type
                && edge.Source.Name == "Billing.Application.DeconstructionConsumer" && edge.Target.Name == "Helpers.UnusedExtensions");
        }
        finally
        {
            File.Delete(sourcePath);
            File.Delete(targetPath);
        }
    }

    /// <summary>Retains project ownership and source evidence for top-level statements without requiring an authored type.</summary>
    [Fact]
    public async Task Top_level_statements_preserve_cross_project_evidence()
    {
        var root = Path.Combine(fixture.Root, "TopLevel");
        Directory.CreateDirectory(root);
        var projectPath = Path.Combine(root, "TopLevel.csproj");
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup>
              <ItemGroup><ProjectReference Include="../ModularMonolith/src/Billing/Billing.Contracts/Billing.Contracts.csproj" /></ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(root, "Program.cs"), "Billing.Contracts.IInvoice? invoice = null; System.Console.WriteLine(invoice?.Total);");
        await fixture.RestoreAsync(projectPath);
        var policy = new ArchitecturePolicy([new("All", ["**/*.csproj"])], [new("All", ["**/*.csproj"])],
            new Dictionary<string, DiagnosticSeverity> { ["AG001"] = DiagnosticSeverity.Error });
        using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(new(projectPath, true));
        var result = await new DependencyGraphBuilder().BuildAsync(workspace, fixture.Root, policy);
        Complete(result);
        Assert.Contains(result.Graph!.Edges, edge => edge.Kind == DependencyKind.Assembly
            && edge.Source.ProjectPath == "TopLevel/TopLevel.csproj" && edge.Target.ProjectPath == "ModularMonolith/" + Contracts
            && edge.Evidence.Any(evidence => evidence.Fact.Location?.Path == "TopLevel/Program.cs"));
    }

    /// <summary>Uses logical generated evidence paths and file-local identities across configurations and output directories.</summary>
    [Fact]
    public async Task Generated_dependencies_use_portable_generator_scoped_evidence()
    {
        var projectPath = Path.Combine(fixture.Root, "GeneratorConsumer", "GeneratorConsumer.csproj");
        var original = await File.ReadAllTextAsync(projectPath);
        var output = Path.Combine(Path.GetTempPath(), "arch-guard-graph-output-" + Guid.NewGuid().ToString("N"));
        var policy = new ArchitecturePolicy([new("All", ["**/*.csproj"])], [new("All", ["**/*.csproj"])],
            new Dictionary<string, DiagnosticSeverity> { ["AG001"] = DiagnosticSeverity.Error });
        try
        {
            string[]? expected = null;
            foreach (var (configuration, externalOutput) in new[] { ("Debug", false), ("Release", true) })
            {
                if (externalOutput)
                {
                    var project = XDocument.Parse(original);
                    project.Root!.Elements("PropertyGroup").First().Add(new XElement("IntermediateOutputPath", output + Path.DirectorySeparatorChar));
                    await File.WriteAllTextAsync(projectPath, project.ToString());
                    await fixture.RestoreAsync(projectPath);
                }

                using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(new(projectPath, true, configuration));
                var result = await new DependencyGraphBuilder().BuildAsync(workspace, fixture.Root, policy);
                Complete(result);
                var uses = result.Graph!.Edges.Where(edge => edge.Kind == DependencyKind.Type
                    && edge.Source.Name == "Generated.LocalHelper" && edge.Target.Name == "System.Text.StringBuilder").ToArray();
                Assert.Equal(3, uses.Length);
                Assert.Equal(3, uses.Select(edge => edge.Source.Id).Distinct().Count());
                Assert.All(uses.SelectMany(edge => edge.Evidence), evidence => Assert.StartsWith("generated/GeneratorConsumer/GeneratorConsumer.csproj/", evidence.Fact.Location!.Path));
                // SDK-generated assembly attributes are configuration-specific compilation inputs.
                // Source-generator evidence must be stable independently of those authored-tree changes.
                var snapshot = Snapshot(new DependencyGraph([], uses));
                if (expected is null)
                {
                    expected = snapshot;
                }
                else
                {
                    Assert.Equal(expected, snapshot);
                }
            }
        }
        finally
        {
            await File.WriteAllTextAsync(projectPath, original);
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    /// <summary>Surfaces unresolved types, dynamic calls and loading failures without hiding the available graph.</summary>
    [Fact]
    public async Task Missing_semantic_context_is_incomplete_and_cancellation_stays_distinct()
    {
        var root = Path.Combine(fixture.Root, "ModularMonolith");
        var sourcePath = Path.Combine(root, "src/Orders/Orders.Application/Broken.cs");
        try
        {
            await File.WriteAllTextAsync(sourcePath, "namespace Orders.Application; public class Broken { public Missing.Type? Value; public void Run(dynamic value) { value.Execute(); } }");
            var result = await BuildAsync();
            Assert.Equal(AnalysisStatus.Incomplete, result.Status);
            Assert.NotNull(result.Graph);
            Assert.Contains(result.Issues, issue => issue.Code == "dependency-semantic-error" && issue.Location?.Path.EndsWith("Broken.cs", StringComparison.Ordinal) == true);
            Assert.Contains(result.Issues, issue => issue.Code == "dependency-dynamic-unresolved");
            Assert.Contains(result.Issues, issue => issue.Code == "dependency-type-unresolved");
            Assert.Contains(result.Graph.Edges, edge => edge.Kind == DependencyKind.Project);
        }
        finally
        {
            File.Delete(sourcePath);
        }

        var policy = await new YamlPolicyLoader().LoadAsync(Path.Combine(root, "architecture.guardian.yaml"));
        using var unavailable = await new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(root, "ModularMonolith.slnx")));
        var missing = await new DependencyGraphBuilder().BuildAsync(unavailable, root, policy);
        Assert.Equal(AnalysisStatus.Incomplete, missing.Status);
        Assert.Null(missing.Graph);
        Assert.Contains(missing.Issues, issue => issue.Code == "workspace-untrusted");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancellation = await new DependencyGraphBuilder().BuildAsync(unavailable, root, policy, cancelled.Token);
        Assert.Equal(AnalysisStatus.Cancelled, cancellation.Status);
    }

    /// <summary>Does not mistake successful compilation of runtime-bound operators/conversions for complete static coverage.</summary>
    [Fact]
    public async Task Dynamic_operators_and_conversions_reduce_coverage_without_compiler_errors()
    {
        var root = Path.Combine(fixture.Root, "ModularMonolith");
        var sourcePath = Path.Combine(root, "src/Orders/Orders.Application/Dynamic.cs");
        try
        {
            await File.WriteAllTextAsync(sourcePath, """
                namespace Orders.Application;
                public class DynamicUses
                {
                    public dynamic Add(dynamic value) => value + 1;
                    public dynamic Negate(dynamic value) => -value;
                    public int Convert(dynamic value) => (int)value;
                    public async System.Threading.Tasks.Task Await(dynamic value) { await value; }
                }
                """);
            var result = await BuildAsync();
            Assert.Equal(AnalysisStatus.Incomplete, result.Status);
            Assert.DoesNotContain(result.Issues, issue => issue.Code is "dependency-semantic-error" or "compiler-error");
            foreach (var line in new[] { 4, 5, 6, 7 })
            {
                Assert.Contains(result.Issues, issue => issue.Code == "dependency-dynamic-unresolved" && issue.Location?.StartLine == line);
            }
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    /// <summary>Produces identical graph/evidence identities on repeated extraction and after a checkout relocation.</summary>
    [Fact]
    public async Task Relocated_workspaces_preserve_graph_and_evidence_ordering()
    {
        var first = await BuildAsync();
        Complete(first);
        var movedRoot = Path.Combine(Path.GetTempPath(), "arch-guard-graph-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var file in Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(fixture.Root, file);
                if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
                {
                    continue;
                }

                var destination = Path.Combine(movedRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }

            var root = Path.Combine(movedRoot, "ModularMonolith");
            await fixture.RestoreAsync(Path.Combine(root, "ModularMonolith.slnx"));
            var moved = await BuildAsync(root);
            Complete(moved);
            Assert.Equal(Snapshot(first.Graph!), Snapshot(moved.Graph!));
        }
        finally
        {
            if (Directory.Exists(movedRoot))
            {
                Directory.Delete(movedRoot, recursive: true);
            }
        }
    }

    /// <summary>A valid acyclic project graph can still form a logical module cycle through a public contract.</summary>
    [Fact]
    public async Task Module_cycle_retains_real_project_and_semantic_evidence()
    {
        var root = Path.Combine(fixture.Root, "ModularMonolith");
        const string billing = "src/Billing/Billing.Application/Billing.Application.csproj";
        var projectPath = Path.Combine(root, billing);
        var sourcePath = Path.Combine(root, "src/Billing/Billing.Application/OrderUse.cs");
        var original = await File.ReadAllTextAsync(projectPath);
        try
        {
            var xml = XDocument.Parse(original);
            xml.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference",
                new XAttribute("Include", "../../Orders/Orders.Application/Orders.Application.csproj"))));
            await File.WriteAllTextAsync(projectPath, xml.ToString());
            await File.WriteAllTextAsync(sourcePath,
                "namespace Billing.Application; public class OrderUse { public Orders.Application.SubmitOrder? Value { get; set; } }");
            await fixture.RestoreAsync(Path.Combine(root, "ModularMonolith.slnx"));
            var context = await BuildAsync();
            Complete(context);
            var result = await EvaluateDependenciesAsync(context, "AG003");
            Assert.True(result.IsComplete);
            var finding = Assert.Single(result.Diagnostics);
            Assert.Equal("Module dependency cycle: Billing → Orders → Billing.", finding.Message);
            Assert.Contains(finding.Evidence, fact => fact.Description.Contains(billing, StringComparison.Ordinal)
                && fact.Description.Contains(Orders, StringComparison.Ordinal));
            Assert.Contains(finding.Evidence, fact => fact.Description.Contains(Orders, StringComparison.Ordinal)
                && fact.Description.Contains(Contracts, StringComparison.Ordinal));
            Assert.Contains(finding.Evidence, fact => fact.Location?.Path.EndsWith("OrderUse.cs", StringComparison.Ordinal) == true);
            Assert.Contains(finding.Evidence, fact => fact.Location?.Path.EndsWith("SubmitOrder.cs", StringComparison.Ordinal) == true);
        }
        finally
        {
            await File.WriteAllTextAsync(projectPath, original);
            File.Delete(sourcePath);
            // Restore the original assets too: SDK-expanded references otherwise leak into later tests.
            await fixture.RestoreAsync(Path.Combine(root, "ModularMonolith.slnx"));
        }
    }

    private async Task<DependencyBuildResult> BuildAsync(string? root = null)
    {
        root ??= Path.Combine(fixture.Root, "ModularMonolith");
        var policy = await new YamlPolicyLoader().LoadAsync(Path.Combine(root, "architecture.guardian.yaml"));
        using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(root, "ModularMonolith.slnx"), true));
        return await new DependencyGraphBuilder().BuildAsync(workspace, root, policy);
    }

    private static void Complete(DependencyBuildResult result) => Assert.True(result.Status == AnalysisStatus.Completed,
        string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));

    private static Task<AnalysisResult> EvaluateDependenciesAsync(DependencyBuildResult context, string ruleId)
    {
        // Select one rule to isolate its findings from the fixture's other configured checks.
        var policy = context.Repository!.Policy;
        var selection = new ArchitecturePolicy(policy.Modules, policy.Layers,
            new Dictionary<string, DiagnosticSeverity> { [ruleId] = policy.SeverityPolicy.GetSeverity(ruleId) });
        var rules = selection.SelectRules<DependencyBuildResult>([new ModuleDependenciesRule(), new LayerDependenciesRule(), new ModuleCyclesRule()]);
        return new AnalysisService<DependencyBuildResult>(rules).AnalyzeAsync(new(context, selection.SeverityPolicy));
    }

    private static string[] Snapshot(DependencyGraph graph) => [.. graph.Nodes.Select(node => node.Id),
        .. graph.Edges.SelectMany(edge => new[] { edge.Id }.Concat(edge.Evidence.Select(evidence =>
            $"{evidence.SourceProject}|{evidence.TargetProject}|{evidence.SourceSymbol}|{evidence.TargetSymbol}|{evidence.ProjectReference}|{evidence.Fact}")))];
}
