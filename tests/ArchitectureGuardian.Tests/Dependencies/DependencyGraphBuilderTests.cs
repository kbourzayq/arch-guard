using System.Xml.Linq;
using ArchitectureGuardian.Analysis.Dependencies;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Tests.Workspaces;
using Xunit;

namespace ArchitectureGuardian.Tests.Dependencies;

/// <summary>Verifies semantic extraction and evidence against real, isolated MSBuild workspaces.</summary>
/// <param name="fixture">Restored workspace inputs.</param>
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
    }

    /// <summary>Traces a forbidden project and semantic dependency without performing rule evaluation or rendering.</summary>
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
        }
        finally
        {
            await File.WriteAllTextAsync(projectPath, original);
            File.Delete(sourcePath);
            File.Delete(targetPath);
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

    private async Task<DependencyBuildResult> BuildAsync(string? root = null)
    {
        root ??= Path.Combine(fixture.Root, "ModularMonolith");
        var policy = await new YamlPolicyLoader().LoadAsync(Path.Combine(root, "architecture.guardian.yaml"));
        using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(root, "ModularMonolith.slnx"), true));
        return await new DependencyGraphBuilder().BuildAsync(workspace, root, policy);
    }

    private static void Complete(DependencyBuildResult result) => Assert.True(result.Status == AnalysisStatus.Completed,
        string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));

    private static string[] Snapshot(DependencyGraph graph) => [.. graph.Nodes.Select(node => node.Id),
        .. graph.Edges.SelectMany(edge => new[] { edge.Id }.Concat(edge.Evidence.Select(evidence =>
            $"{evidence.SourceProject}|{evidence.TargetProject}|{evidence.SourceSymbol}|{evidence.TargetSymbol}|{evidence.ProjectReference}|{evidence.Fact}")))];
}
