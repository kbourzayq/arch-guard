using ArchitectureGuardian.Analysis.Dependencies;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Frameworks.EntityFrameworkCore;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Rules;
using ArchitectureGuardian.Tests.Workspaces;
using Xunit;
using DiagnosticSeverity = ArchitectureGuardian.Core.Diagnostics.DiagnosticSeverity;

namespace ArchitectureGuardian.Tests.EntityFrameworkCore;

/// <summary>Evaluates EF001–EF003 against a real EF Core workspace with violations, allowed access and composition.</summary>
/// <param name="fixture">Restored EF Core workspace.</param>
[Trait("Category", "Integration")]
public sealed class EntityFrameworkCoreRuleTests(EfCoreWorkspaceFixture fixture) : IClassFixture<EfCoreWorkspaceFixture>
{
    private async Task<DependencyBuildResult> BuildAsync(IEnumerable<string>? rules = null)
    {
        var root = Path.Combine(fixture.Root, "EfCore");
        using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(root, "EfCore.slnx"), isTrusted: true),
            TestContext.Current.CancellationToken);
        Assert.True(workspace.Status == AnalysisStatus.Completed, string.Join('\n', workspace.Issues.Select(item => item.Code + ": " + item.Message)));
        var policy = new ArchitecturePolicy(
            [new("Orders", ["Orders/**/*.csproj"]), new("Billing", ["Billing/**/*.csproj"]), new("Host", ["Host/*.csproj"])],
            [
                new("Domain", ["Orders/Orders.Domain/*.csproj"]), new("Application", ["Orders/Orders.Application/*.csproj"]),
                new("Infrastructure", ["Orders/Orders.Infrastructure/*.csproj", "Billing/**/*.csproj"]), new("Api", ["Host/*.csproj"]),
            ],
            (rules ?? EntityFrameworkCoreRuleCatalog.All.Select(rule => rule.Id)).ToDictionary(id => id, _ => DiagnosticSeverity.Error),
            entityFrameworkCore: new EntityFrameworkCorePolicy(["Infrastructure"], ["AddDbContextPool", "FromSqlRaw", "IgnoreQueryFilters"]));
        var context = await new DependencyGraphBuilder().BuildAsync(workspace, root, policy, TestContext.Current.CancellationToken);
        Assert.True(context.Status == AnalysisStatus.Completed, string.Join('\n', context.Issues.Select(item => item.Code + ": " + item.Message)));
        return context;
    }

    private static async Task<IReadOnlyList<RuleViolation>> EvaluateAsync(DependencyBuildResult context, string id)
    {
        var result = await SolutionRules.All.Single(rule => rule.Descriptor.Id == id).EvaluateAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisStatus.Completed, result.Status);
        return result.Violations;
    }

    private static string[] Targets(IEnumerable<RuleViolation> violations, ViolationClassification classification) => violations
        .Where(item => item.Classification == classification).Select(item => item.Target.Symbol!).Order(StringComparer.Ordinal).ToArray();

    /// <summary>EF001 proves cross-module DbContext use, requires runtime evidence for the base type, and allows owners and composition.</summary>
    [Fact]
    public async Task DbContext_ownership_separates_proven_and_runtime_findings()
    {
        var violations = await EvaluateAsync(await BuildAsync(), "EF001");
        // Constructor parameters, accessor return types, indexers, aliases and registration callbacks all prove Billing's ownership.
        Assert.Equal(["Host.Composition", "Orders.Infrastructure.AccessorReport", "Orders.Infrastructure.AliasReport",
            "Orders.Infrastructure.CrossModuleReport", "Orders.Infrastructure.IndexerReport"], Targets(violations, ViolationClassification.Proven));
        // Only the callback's context use is reported for the host; registered context types and option configuration are not.
        Assert.All(violations.Single(item => item.Target.Symbol == "Host.Composition").Evidence,
            fact => Assert.Contains("Billing.Infrastructure.BillingDbContext", fact.Description, StringComparison.Ordinal));
        Assert.Equal(["Orders.Infrastructure.GenericRepository"], Targets(violations, ViolationClassification.RuntimeValidationRequired));
        Assert.Empty(Targets(violations, ViolationClassification.Potential));
        var proven = violations.Single(item => item.Target.Symbol == "Orders.Infrastructure.CrossModuleReport");
        Assert.Equal("Orders", proven.Target.Module);
        Assert.Contains("module 'Billing' owns", proven.Message, StringComparison.Ordinal);
        Assert.Equal("Billing", Assert.Single(proven.Context!.PolicyConstraints).Values.Single());
        Assert.All(proven.Evidence, fact => Assert.Equal("Orders/Orders.Infrastructure/Persistence.cs", fact.Location!.Path));
    }

    /// <summary>EF002 proves EF Core use outside persistence layers; repository contracts, lookalikes and registrations pass.</summary>
    [Fact]
    public async Task Persistence_layers_allow_repository_contracts_and_composition()
    {
        var violations = await EvaluateAsync(await BuildAsync(), "EF002");
        // The host's registration callback uses a context outside the persistence layers; its options configuration does not count.
        Assert.Equal(["Host.Composition", "Orders.Application.LeakyOrderQuery"], violations.Select(item => item.Target.Symbol!).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(["Billing.Infrastructure.BillingDbContext"], violations.Where(item => item.Target.Symbol == "Host.Composition")
            .Select(item => item.Context!.DependencyPath[1].Identity));
        Assert.All(violations, item => Assert.Equal(ViolationClassification.Proven, item.Classification));
        Assert.Contains(violations, item => item.Message.Contains("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions", StringComparison.Ordinal));
    }

    /// <summary>EF003 scopes IgnoreQueryFilters as potential policy evidence and proves other forbidden APIs, wherever EF Core declares them.</summary>
    [Fact]
    public async Task Forbidden_apis_scope_ignore_query_filters_as_policy_evidence()
    {
        var violations = await EvaluateAsync(await BuildAsync(), "EF003");
        var potential = Assert.Single(violations, item => item.Classification == ViolationClassification.Potential);
        Assert.Equal("Orders.Infrastructure.AdminOrderQueries", potential.Target.Symbol);
        Assert.Contains("IgnoreQueryFilters", potential.Message, StringComparison.Ordinal);
        Assert.Contains("does not prove that tenant or other filtered data is exposed", potential.Message, StringComparison.Ordinal);
        var proven = violations.Where(item => item.Classification == ViolationClassification.Proven).ToArray();
        // AddDbContextPool ships in EF Core but is declared in Microsoft.Extensions.DependencyInjection; the source lookalike is ignored.
        Assert.Equal(["Host.Composition", "Orders.Infrastructure.AdminOrderQueries"], proven.Select(item => item.Target.Symbol!).Order(StringComparer.Ordinal));
        Assert.Contains("FromSqlRaw", proven.Single(item => item.Target.Symbol == "Orders.Infrastructure.AdminOrderQueries").Message, StringComparison.Ordinal);
        Assert.All(violations.SelectMany(item => item.Evidence), fact => Assert.Contains("forbidden EF Core API", fact.Description, StringComparison.Ordinal));
        // Each finding records only its matched API, so banning another API keeps existing baseline fingerprints.
        Assert.All(violations, item => Assert.Equal([item.Context!.DependencyPath[1].Identity], Assert.Single(item.Context.PolicyConstraints).Values));
    }

    /// <summary>Selecting only a non-EF rule skips fact extraction, while selecting an EF rule without facts is incomplete.</summary>
    [Fact]
    public async Task Facts_are_extracted_only_for_selected_ef_rules_and_missing_facts_are_incomplete()
    {
        var context = await BuildAsync(["AG001"]);
        Assert.Null(context.EntityFrameworkCore);
        var result = await new DbContextOwnershipRule().EvaluateAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Equal("efcore-facts-unavailable", Assert.Single(result.Issues).Code);
    }

    /// <summary>Cancellation during evaluation propagates instead of yielding a reduced clean result.</summary>
    [Fact]
    public async Task Cancellation_propagates_from_evaluation()
    {
        var context = await BuildAsync();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PersistenceLayersRule().EvaluateAsync(context, cancellation.Token));
    }

    /// <summary>Findings are identical across repeated runs, so baselines and reports stay reproducible.</summary>
    [Fact]
    public async Task Findings_are_deterministic()
    {
        var first = await BuildAsync();
        var second = await BuildAsync();
        foreach (var rule in EntityFrameworkCoreRuleCatalog.All)
        {
            Assert.Equal((await EvaluateAsync(first, rule.Id)).Select(item => item.Id), (await EvaluateAsync(second, rule.Id)).Select(item => item.Id));
        }
    }
}
