using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Discovery;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Repositories;
using Xunit;

namespace ArchitectureGuardian.Tests.Discovery;

/// <summary>Verifies conservative intentions against independently specified ownership and permission expectations.</summary>
public sealed class PolicyProposalTests
{
    /// <summary>Uses role evidence for ownership while preserving denied reverse and cross-module dependencies.</summary>
    [Fact]
    public void Conventional_roles_produce_directional_permissions_and_reviewable_contracts()
    {
        var projects = new[] { Project("Shop.Domain"), Project("Shop.Application"), Project("Shop.Infrastructure"), Project("Shop.Contracts"), Project("Orders.Application") };
        var proposal = new PolicyProposalBuilder().Build(Observed(projects), TestContext.Current.CancellationToken);
        Assert.NotNull(proposal.Policy);
        var resolved = new ResolvedPolicy(proposal.Policy, projects.Select(project => project.Id));
        Assert.Equal("Shop", resolved.Projects["src/Shop.Domain.csproj"].ModuleId);
        Assert.Equal("api", resolved.Projects["src/Shop.Contracts.csproj"].ContractId);
        Assert.Equal(new[] { "Contracts", "Domain" }, proposal.Policy.Layers.Single(layer => layer.Id == "Application").AllowedDependencies);
        Assert.Empty(proposal.Policy.Layers.Single(layer => layer.Id == "Domain").AllowedDependencies);
        Assert.True(resolved.AllowsLayerDependency("src/Shop.Application.csproj", "src/Shop.Domain.csproj"));
        Assert.False(resolved.AllowsLayerDependency("src/Shop.Domain.csproj", "src/Shop.Infrastructure.csproj"));
        Assert.False(resolved.AllowsModuleDependency("src/Orders.Application.csproj", "src/Shop.Contracts.csproj"));
        Assert.All(proposal.Policy.Modules, module => Assert.Empty(module.AllowedDependencies));
        Assert.Equal(new[] { "AG001", "AG002", "AG003", "AG004", "AG005", "AG006" }, proposal.Policy.SeverityPolicy.RuleSeverities.Keys.Order(StringComparer.Ordinal));
        Assert.NotEmpty(proposal.Policy.Layers.Single(layer => layer.Id == "Domain").DomainInfrastructure!.ForbiddenNamespaces);
        Assert.Contains(proposal.Inferences, item => item.Kind == "style" && item.Subject == "Clean Architecture");
        Assert.Contains(proposal.Inferences, item => item.Kind == "style" && item.Subject == "Modular Monolith");
        Assert.Contains(proposal.Inferences, item => item.Kind == "contract" && item.Confidence == "medium");
    }

    /// <summary>Accepts nonstandard paths with corroborating namespaces, without using assembly identity as ownership.</summary>
    [Fact]
    public void Legacy_names_and_custom_assemblies_remain_separate_from_intentions()
    {
        var project = Project("legacy", "Shop.Domain", "Different.Output", "old/storage/legacy.csproj");
        var proposal = new PolicyProposalBuilder().Build(Observed([project]), TestContext.Current.CancellationToken);
        Assert.NotNull(proposal.Policy);
        Assert.Equal(new DiscoveryOwnership(project.Id, "Shop", "Domain", null), Assert.Single(proposal.Ownership));
        Assert.Contains(proposal.Inferences, item => item.Kind == "naming-inconsistency" && item.Evidence.Contains("Evaluated assembly: Different.Output"));
    }

    /// <summary>Leaves absent or conflicting role/module signals unresolved and retains every observed project.</summary>
    /// <param name="name">Project filename signal.</param>
    /// <param name="ns">Namespace signal.</param>
    [Theory]
    [InlineData("Legacy", "Unstructured")]
    [InlineData("Shop.Domain", "Shop.Infrastructure")]
    [InlineData("Shop.Domain", "Other.Domain")]
    public void Ambiguous_or_absent_ownership_never_silently_drops_projects(string name, string ns)
    {
        var observations = Observed([Project(name, ns), Project("Shop.Application")]);
        var proposal = new PolicyProposalBuilder().Build(observations, TestContext.Current.CancellationToken);
        Assert.Null(proposal.Policy);
        Assert.Equal(2, observations.Inventory!.Projects.Count);
        Assert.Contains(proposal.Questions, question => question.StartsWith("src/" + name + ".csproj:", StringComparison.Ordinal));
    }

    /// <summary>Reports feature namespaces as a style hint with an explicit project-schema limitation.</summary>
    [Fact]
    public void Feature_namespaces_are_evidence_not_namespace_ownership()
    {
        var proposal = new PolicyProposalBuilder().Build(Observed([Project("Shop.Application", "Shop.Application.Features.Orders")]), TestContext.Current.CancellationToken);
        Assert.Contains(proposal.Inferences, item => item.Subject == "Vertical Slice" && item.Confidence == "low");
        Assert.Contains(proposal.Questions, question => question.Contains("Namespace/feature slices", StringComparison.Ordinal));
        Assert.Equal("src/Shop.Application.csproj", Assert.Single(proposal.Ownership).ProjectPath);
    }

    /// <summary>Test project suffixes describe a test role even when the tested layer appears in the name.</summary>
    [Fact]
    public void Test_projects_keep_explicit_ownership()
    {
        var proposal = new PolicyProposalBuilder().Build(Observed([Project("Shop.Domain.Tests")]), TestContext.Current.CancellationToken);
        Assert.Equal("Tests", Assert.Single(proposal.Ownership).Layer);
        Assert.Equal("Shop", Assert.Single(proposal.Ownership).Module);
        Assert.NotNull(proposal.Policy);
    }

    /// <summary>Preserves noncompleted coverage rather than proposing from a partial but plausible inventory.</summary>
    /// <param name="status">Noncompleted coverage.</param>
    [Theory]
    [InlineData(AnalysisStatus.Incomplete)]
    [InlineData(AnalysisStatus.Failed)]
    [InlineData(AnalysisStatus.Cancelled)]
    public void Noncompleted_evidence_cannot_produce_a_policy(AnalysisStatus status)
    {
        var observations = Observed([Project("Shop.Domain")]);
        var result = new DependencyInventoryResult(observations.Inventory, observations.Graph, status,
            status == AnalysisStatus.Cancelled ? [] : [new("missing-context", "Complete the selected context.")]);
        var proposal = new PolicyProposalBuilder().Build(result, TestContext.Current.CancellationToken);
        Assert.Null(proposal.Policy);
        Assert.NotEmpty(proposal.Questions);
    }

    /// <summary>Large inventories and reversed enumeration preserve exact ownership without broad globs.</summary>
    [Fact]
    public void Large_inventories_are_order_independent_and_cancellable()
    {
        var projects = Enumerable.Range(0, 1500).Select(index => Project("Module" + index + ".Domain")).ToArray();
        var first = new PolicyProposalBuilder().Build(Observed(projects), TestContext.Current.CancellationToken);
        var second = new PolicyProposalBuilder().Build(Observed(projects.Reverse()), TestContext.Current.CancellationToken);
        Assert.Equal(1500, first.Ownership.Count);
        Assert.Equal(first.Ownership, second.Ownership);
        Assert.All(first.Policy!.Modules, module => Assert.DoesNotContain('*', Assert.Single(module.Projects).Value));
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => new PolicyProposalBuilder().Build(Observed(projects), source.Token));
    }

    private static DependencyInventoryResult Observed(IEnumerable<RepositoryProject> projects) =>
        new(new RepositoryInventory("App.slnx", projects), new DependencyGraph([], []), AnalysisStatus.Completed);

    private static RepositoryProject Project(string name, string? ns = null, string? assembly = null, string? path = null)
    {
        path ??= "src/" + name + ".csproj";
        ns ??= name;
        return new(path, name, new(path, assembly ?? name,
            [new RepositoryNamespace(path, ns, [new RepositoryType(path, ns, ns + ".Contract", RepositoryTypeKind.Interface, true)])]));
    }
}
