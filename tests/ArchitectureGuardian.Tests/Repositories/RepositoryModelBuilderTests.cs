using System.Xml.Linq;
using ArchitectureGuardian.Analysis.Repositories;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Repositories;
using ArchitectureGuardian.Tests.Workspaces;
using Xunit;

namespace ArchitectureGuardian.Tests.Repositories;

/// <summary>Verifies real workspace normalization, multi-project modules, portable identities and coverage propagation.</summary>
/// <param name="fixture">Restored, isolated workspace fixtures.</param>
[Trait("Category", "Integration")]
public sealed class RepositoryModelBuilderTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private const string Contracts = "src/Billing/Billing.Contracts/Billing.Contracts.csproj";
    private const string Infrastructure = "src/Billing/Billing.Infrastructure/Billing.Infrastructure.csproj";
    private const string Orders = "src/Orders/Orders.Application/Orders.Application.csproj";

    /// <summary>Models one module across four layers and demonstrates permitted contract access through a real project reference.</summary>
    [Fact]
    public async Task Multi_project_modules_keep_contract_and_layer_membership()
    {
        var root = Path.Combine(fixture.Root, "ModularMonolith");
        var result = await BuildAsync(root);
        AssertComplete(result);
        var model = result.Model!;
        Assert.Equal("ModularMonolith.slnx", model.Id);
        Assert.Equal(5, model.Projects.Count);
        var billing = Assert.Single(model.Modules, module => module.Id == "Billing");
        Assert.Equal(4, billing.Projects.Count);
        Assert.Equal(new[] { "Application", "Contracts", "Domain", "Infrastructure" },
            billing.Projects.Select(project => model.ResolvedPolicy.Projects[project.Id].LayerId).Order(StringComparer.Ordinal));
        var contract = Assert.Single(billing.Contracts);
        Assert.Equal("Billing/api", contract.Id);
        Assert.Equal(Contracts, Assert.Single(contract.Projects).Id);
        Assert.Equal("Company.Billing.Messages", Assert.Single(model.Projects, project => project.Id == Contracts).Assembly.Name);
        Assert.Equal(2, Assert.Single(model.Layers, layer => layer.Id == "Application").Projects.Count);
        Assert.True(model.ResolvedPolicy.AllowsModuleDependency(Orders, Contracts));
        Assert.True(model.ResolvedPolicy.AllowsLayerDependency(Orders, Contracts));
        Assert.False(model.ResolvedPolicy.AllowsModuleDependency(Orders, Infrastructure));
        Assert.False(model.ResolvedPolicy.AllowsLayerDependency(Orders, Infrastructure));
        Assert.Throws<NotSupportedException>(() => ((IList<RepositoryContract>)billing.Contracts).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<RepositoryProject>)contract.Projects).Clear());

        using var workspace = await LoadAsync(root);
        var source = Assert.Single(workspace.Solution!.Projects, project => project.Name == "Orders.Application");
        var reference = Assert.Single(source.ProjectReferences);
        Assert.Equal("Billing.Contracts", workspace.Solution.GetProject(reference.ProjectId)!.Name);
        var compilation = await source.GetCompilationAsync();
        Assert.NotNull(compilation!.GetTypeByMetadataName("Billing.Contracts.IInvoice"));
    }

    /// <summary>Includes partial, nested, generic, file-local and global types once, without referenced framework declarations.</summary>
    [Fact]
    public async Task Source_symbols_are_normalized_without_compiler_objects()
    {
        var result = await BuildAsync(Path.Combine(fixture.Root, "ModularMonolith"));
        AssertComplete(result);
        var model = result.Model!;
        var domain = Assert.Single(model.Projects, project => project.Name == "Billing.Domain");
        var types = domain.Assembly.Namespaces.SelectMany(ns => ns.Types).ToArray();
        Assert.True(Assert.Single(types, type => type.MetadataName == "Billing.Domain.Invoice`1").IsPublic);
        Assert.True(Assert.Single(types, type => type.MetadataName == "Billing.Domain.Invoice`1+Line`1").IsPublic);
        Assert.False(Assert.Single(types, type => type.MetadataName == "Billing.Domain.Invoice`1+PrivateLine").IsPublic);
        Assert.False(Assert.Single(types, type => type.MetadataName == "Billing.Domain.Hidden+Nested").IsPublic);
        Assert.Equal(RepositoryTypeKind.Struct, Assert.Single(types, type => type.MetadataName == "Billing.Domain.Money").Kind);
        Assert.Equal(RepositoryTypeKind.Class, Assert.Single(types, type => type.MetadataName == "Billing.Domain.InvoiceRecord").Kind);
        Assert.Equal(RepositoryTypeKind.Enum, Assert.Single(types, type => type.MetadataName == "Billing.Domain.InvoiceState").Kind);
        Assert.Equal(RepositoryTypeKind.Delegate, Assert.Single(types, type => type.MetadataName == "Billing.Domain.InvoiceChanged").Kind);
        Assert.Equal("", Assert.Single(types, type => type.MetadataName == "GlobalType").NamespaceName);
        var locals = types.Where(type => type.MetadataName == "Billing.Domain.LocalHelper").ToArray();
        Assert.Equal(2, locals.Length);
        Assert.Equal(2, locals.Select(type => type.Id).Distinct().Count());
        Assert.All(locals, type => Assert.StartsWith("src/Billing/Billing.Domain/", type.DeclaringFile));
        Assert.NotNull(Assert.Single(types, type => type.MetadataName == "Billing.Domain.LocalHelper+Nested").DeclaringFile);
        Assert.DoesNotContain(model.Projects.SelectMany(project => project.Assembly.Namespaces), ns => ns.Name.StartsWith("System", StringComparison.Ordinal));
        Assert.Equal(RepositoryTypeKind.Interface, Assert.Single(model.Projects.Single(project => project.Id == Contracts)
            .Assembly.Namespaces.SelectMany(ns => ns.Types)).Kind);
    }

    /// <summary>Rebuilds the fixture in another checkout directory and compares every identity and collection ordering.</summary>
    [Fact]
    public async Task Relocating_a_checkout_preserves_all_model_identities()
    {
        var original = await BuildAsync(Path.Combine(fixture.Root, "ModularMonolith"));
        AssertComplete(original);
        var relocated = Path.Combine(Path.GetTempPath(), "arch-guard-relocated-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(relocated);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(fixture.Root, "ModularMonolith"), "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(fixture.Root, file);
                if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
                {
                    continue;
                }

                var destination = Path.Combine(relocated, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }

            foreach (var name in new[] { "Directory.Build.props", "NuGet.Config", "global.json" })
            {
                File.Copy(Path.Combine(fixture.Root, name), Path.Combine(relocated, name));
            }

            await fixture.RestoreAsync(Path.Combine(relocated, "ModularMonolith", "ModularMonolith.slnx"));
            var moved = await BuildAsync(Path.Combine(relocated, "ModularMonolith"));
            AssertComplete(moved);
            Assert.Equal(Identities(original.Model!), Identities(moved.Model!));
            // No workspace survives BuildAsync; the neutral model can also be reconstructed from reversed input.
            var reversed = new RepositoryModel(moved.Model!.Id, moved.Model.Policy, moved.Model.Projects.Reverse());
            Assert.Equal(Identities(moved.Model), Identities(reversed));
        }
        finally
        {
            Directory.Delete(relocated, recursive: true);
        }
    }

    /// <summary>Surfaces unmapped projects, ambiguous modules, unused selectors and out-of-root projects as incomplete coverage.</summary>
    [Fact]
    public async Task Invalid_workspace_policy_pairings_have_actionable_issues()
    {
        var root = Path.Combine(fixture.Root, "ModularMonolith");
        var loader = new YamlPolicyLoader();
        var yaml = await File.ReadAllTextAsync(Path.Combine(root, "architecture.guardian.yaml"));
        using var workspace = await LoadAsync(root);
        foreach (var invalid in new[]
        {
            yaml.Replace("src/Orders/**/*.csproj", "src/**/*.csproj", StringComparison.Ordinal),
            yaml.Replace("src/Billing/**/*.csproj", "src/Billing/**/*.Contracts.csproj", StringComparison.Ordinal),
            yaml.Replace("src/Orders/**/*.csproj", "missing.csproj", StringComparison.Ordinal),
        })
        {
            var result = await new RepositoryModelBuilder().BuildAsync(workspace, root, loader.Parse(invalid));
            Assert.Equal(AnalysisStatus.Incomplete, result.Status);
            Assert.Null(result.Model);
            Assert.Contains(result.Issues, issue => issue.Code == "repository-mapping-invalid" && issue.Message.Contains("project", StringComparison.Ordinal));
        }

        var outside = await new RepositoryModelBuilder().BuildAsync(workspace, Path.Combine(root, "src"), loader.Parse(yaml));
        Assert.Equal(AnalysisStatus.Incomplete, outside.Status);
        Assert.Contains(outside.Issues, issue => issue.Code == "repository-mapping-invalid" && issue.Message.Contains("policy directory", StringComparison.Ordinal));
    }

    /// <summary>Retains existing workspace failures and cancellation rather than publishing completed normalization.</summary>
    [Fact]
    public async Task Incomplete_and_cancelled_loading_never_become_complete_models()
    {
        var policy = new ArchitecturePolicy([new("All", ["**/*.csproj"])], [new("All", ["**/*.csproj"])],
            new Dictionary<string, DiagnosticSeverity> { ["AG001"] = DiagnosticSeverity.Error });
        using var broken = await new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(fixture.Root, "SolutionReference.slnx"), true));
        var partial = await new RepositoryModelBuilder().BuildAsync(broken, fixture.Root, policy);
        Assert.Equal(AnalysisStatus.Incomplete, partial.Status);
        Assert.True(partial.Model is not null, string.Join(Environment.NewLine, partial.Issues.Select(issue => issue.Message)));
        Assert.All(broken.Issues, issue => Assert.Contains(issue, partial.Issues));

        using var unavailable = await new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(fixture.Root, "Fixture.slnx")));
        var missing = await new RepositoryModelBuilder().BuildAsync(unavailable, fixture.Root, policy);
        Assert.Equal(AnalysisStatus.Incomplete, missing.Status);
        Assert.Null(missing.Model);
        Assert.Contains(missing.Issues, issue => issue.Code == "workspace-untrusted");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await new RepositoryModelBuilder().BuildAsync(broken, fixture.Root, policy, cancellation.Token);
        Assert.Equal(AnalysisStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.Model);
        using var cancelledLoad = await new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(fixture.Root, "Fixture.slnx")), cancellation.Token);
        Assert.Equal(AnalysisStatus.Cancelled, (await new RepositoryModelBuilder().BuildAsync(cancelledLoad, fixture.Root, policy)).Status);
    }

    /// <summary>Retains the project input identity and includes referenced projects in the neutral inventory.</summary>
    [Fact]
    public async Task Project_inputs_normalize_their_referenced_source_projects()
    {
        using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(fixture.Root, "App", "App.csproj"), true));
        var policy = new ArchitecturePolicy([new("All", ["**/*.csproj"])], [new("All", ["**/*.csproj"])],
            new Dictionary<string, DiagnosticSeverity> { ["AG001"] = DiagnosticSeverity.Error });
        var result = await new RepositoryModelBuilder().BuildAsync(workspace, fixture.Root, policy);
        AssertComplete(result);
        Assert.Equal("App/App.csproj", result.Model!.Id);
        Assert.Equal(2, result.Model.Projects.Count);
    }

    /// <summary>Includes explicit generator output while excluding referenced metadata and implicit compiler helper types.</summary>
    [Fact]
    public async Task Generated_source_declarations_are_included()
    {
        using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(fixture.Root, "GeneratorConsumer", "GeneratorConsumer.csproj"), true));
        var policy = new ArchitecturePolicy([new("All", ["**/*.csproj"])], [new("All", ["**/*.csproj"])],
            new Dictionary<string, DiagnosticSeverity> { ["AG001"] = DiagnosticSeverity.Error });
        var result = await new RepositoryModelBuilder().BuildAsync(workspace, fixture.Root, policy);
        AssertComplete(result);
        Assert.Contains(result.Model!.Projects.SelectMany(project => project.Assembly.Namespaces).SelectMany(ns => ns.Types),
            type => type.MetadataName == "Generated.Contract");
    }

    /// <summary>Uses actual metadata namespace components rather than C# keyword escaping.</summary>
    [Fact]
    public async Task Keyword_namespace_and_type_names_resolve_as_metadata_names()
    {
        var root = Path.Combine(fixture.Root, "ModularMonolith");
        using var workspace = await LoadAsync(root);
        var policy = await new YamlPolicyLoader().LoadAsync(Path.Combine(root, "architecture.guardian.yaml"));
        var result = await new RepositoryModelBuilder().BuildAsync(workspace, root, policy);
        AssertComplete(result);
        var project = Assert.Single(result.Model!.Projects, project => project.Name == "Billing.Domain");
        var ns = Assert.Single(project.Assembly.Namespaces, ns => ns.Name == "class.namespace");
        Assert.Equal(new[] { "class.namespace.event`1", "class.namespace.event`1+int" }, ns.Types.Select(type => type.MetadataName).Order(StringComparer.Ordinal));
        var compilation = await workspace.Solution!.Projects.Single(project => project.Name == "Billing.Domain").GetCompilationAsync();
        Assert.All(ns.Types, type => Assert.NotNull(compilation!.GetTypeByMetadataName(type.MetadataName)));
    }

    /// <summary>Generated file-local identities depend on generator and hint, not configuration or intermediate output directories.</summary>
    [Fact]
    public async Task Generated_file_local_identities_survive_configuration_and_output_path_changes()
    {
        var projectPath = Path.Combine(fixture.Root, "GeneratorConsumer", "GeneratorConsumer.csproj");
        var original = await File.ReadAllTextAsync(projectPath);
        var output = Path.Combine(Path.GetTempPath(), "arch-guard-generated-" + Guid.NewGuid().ToString("N"));
        var policy = new ArchitecturePolicy([new("All", ["**/*.csproj"])], [new("All", ["**/*.csproj"])],
            new Dictionary<string, DiagnosticSeverity> { ["AG001"] = DiagnosticSeverity.Error });
        try
        {
            string[]? expected = null;
            foreach (var (configuration, externalOutput) in new[] { ("Debug", false), ("Release", false), ("Release", true) })
            {
                if (externalOutput)
                {
                    var project = XDocument.Parse(original);
                    project.Root!.Elements("PropertyGroup").First().Add(new XElement("IntermediateOutputPath", output + Path.DirectorySeparatorChar));
                    await File.WriteAllTextAsync(projectPath, project.ToString());
                    await fixture.RestoreAsync(projectPath);
                }

                using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(new(projectPath, true, configuration));
                Assert.True(workspace.Status == AnalysisStatus.Completed, string.Join(Environment.NewLine, workspace.Issues.Select(issue => issue.Message)));
                var result = await new RepositoryModelBuilder().BuildAsync(workspace, fixture.Root, policy);
                AssertComplete(result);
                var types = result.Model!.Projects.SelectMany(project => project.Assembly.Namespaces).SelectMany(ns => ns.Types)
                    .Where(type => type.MetadataName.StartsWith("Generated.LocalHelper", StringComparison.Ordinal)).ToArray();
                Assert.Equal(4, types.Length);
                Assert.Equal(3, types.Count(type => type.MetadataName == "Generated.LocalHelper"));
                Assert.Equal(types.Length, types.Select(type => type.Id).Distinct(StringComparer.Ordinal).Count());
                Assert.All(types, type =>
                {
                    Assert.Null(type.DeclaringFile);
                    Assert.NotNull(type.GeneratedFile);
                    Assert.Equal("FixtureGenerator", type.GeneratedFile.GeneratorAssemblyName);
                });
                Assert.Equal(2, types.Select(type => type.GeneratedFile!.GeneratorTypeName).Distinct(StringComparer.Ordinal).Count());
                Assert.Contains(types, type => type.GeneratedFile!.HintName == "Helpers/Other.g.cs");
                if (externalOutput)
                {
                    var documents = await workspace.Solution!.Projects.Single().GetSourceGeneratedDocumentsAsync();
                    Assert.NotEmpty(documents);
                    Assert.All(documents, document => Assert.StartsWith(".." + Path.DirectorySeparatorChar,
                        Path.GetRelativePath(fixture.Root, document.FilePath!)));
                }

                var identities = types.Select(type => type.Id).Order(StringComparer.Ordinal).ToArray();
                if (expected is null)
                {
                    expected = identities;
                }
                else
                {
                    Assert.Equal(expected, identities);
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

    private static Task<WorkspaceLoadResult> LoadAsync(string root) => new MSBuildWorkspaceLoader()
        .LoadAsync(new(Path.Combine(root, "ModularMonolith.slnx"), true));

    private static async Task<RepositoryBuildResult> BuildAsync(string root)
    {
        using var workspace = await LoadAsync(root);
        var policy = await new YamlPolicyLoader().LoadAsync(Path.Combine(root, "architecture.guardian.yaml"));
        return await new RepositoryModelBuilder().BuildAsync(workspace, root, policy);
    }

    private static string[] Identities(RepositoryModel model) => [model.Id,
        .. model.Projects.SelectMany(project => new[] { project.Id, project.Assembly.Id }
            .Concat(project.Assembly.Namespaces.SelectMany(ns => new[] { ns.Id }.Concat(ns.Types.Select(type => type.Id))))),
        .. model.Modules.SelectMany(module => new[] { module.Id }.Concat(module.Contracts.Select(contract => contract.Id))),
        .. model.Layers.Select(layer => layer.Id)];

    private static void AssertComplete(RepositoryBuildResult result)
    {
        Assert.True(result.Status == AnalysisStatus.Completed, string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
        Assert.NotNull(result.Model);
        Assert.Empty(result.Issues);
    }
}
