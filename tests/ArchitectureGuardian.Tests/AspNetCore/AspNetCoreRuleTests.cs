using ArchitectureGuardian.Analysis.Dependencies;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Frameworks.AspNetCore;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Rules;
using ArchitectureGuardian.Tests.Workspaces;
using Xunit;
using DiagnosticSeverity = ArchitectureGuardian.Core.Diagnostics.DiagnosticSeverity;

namespace ArchitectureGuardian.Tests.AspNetCore;

/// <summary>Evaluates ASP001–ASP005 against a real ASP.NET Core workspace with supported, allowed and unsupported cases.</summary>
/// <param name="fixture">Restored ASP.NET Core workspace.</param>
[Trait("Category", "Integration")]
public sealed class AspNetCoreRuleTests(AspNetCoreWorkspaceFixture fixture) : IClassFixture<AspNetCoreWorkspaceFixture>
{
    private async Task<DependencyBuildResult> BuildAsync(IEnumerable<string>? rules = null)
    {
        var root = Path.Combine(fixture.Root, "AspNetCore");
        using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(root, "AspNetCore.slnx"), isTrusted: true),
            TestContext.Current.CancellationToken);
        Assert.True(workspace.Status == AnalysisStatus.Completed, string.Join('\n', workspace.Issues.Select(item => item.Code + ": " + item.Message)));
        var policy = new ArchitecturePolicy([new("All", ["**/*.csproj"])], [new("Business", ["**/*.csproj"])],
            (rules ?? AspNetCoreRuleCatalog.All.Select(rule => rule.Id)).ToDictionary(id => id, _ => DiagnosticSeverity.Error));
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

    /// <summary>ASP001 flags constructor and body persistence in controllers only, with proven evidence.</summary>
    [Fact]
    public async Task Controller_persistence_is_proven_from_constructor_and_body_uses()
    {
        var violations = await EvaluateAsync(await BuildAsync(), "ASP001");
        Assert.Equal(["Web.BodyController.Query()", "Web.OrdersController"], violations.Select(item => item.Target.Symbol!).Distinct().Order(StringComparer.Ordinal));
        Assert.Contains(violations, item => item.Message.Contains("System.Data.IDbConnection", StringComparison.Ordinal));
        Assert.All(violations, item =>
        {
            Assert.Equal(ViolationClassification.Proven, item.Classification);
            Assert.All(item.Evidence, fact => Assert.EndsWith("Controllers.cs", fact.Location!.Path, StringComparison.Ordinal));
        });
    }

    /// <summary>ASP004 flags minimal API persistence in the handler parameters and body, not controllers.</summary>
    [Fact]
    public async Task Minimal_api_persistence_is_proven_from_handler_parameters()
    {
        var violations = await EvaluateAsync(await BuildAsync(), "ASP004");
        var violation = violations[0];
        Assert.Equal(["GET /data", "GET /rd"], violations.Select(item => item.Target.Symbol!).Order(StringComparer.Ordinal));
        Assert.Equal(ViolationClassification.Proven, violation.Classification);
    }

    /// <summary>ASP002 distinguishes missing declarations from route groups it cannot resolve; allowed cases pass.</summary>
    [Fact]
    public async Task Authorization_separates_proven_gaps_from_runtime_validation_requirements()
    {
        var violations = await EvaluateAsync(await BuildAsync(), "ASP002");
        var byClass = violations.ToLookup(item => item.Classification);
        Assert.Equal(["GET /fake", "GET /open", "Web.DerivedController.Inherited()", "Web.OpenController.Get()"], byClass[ViolationClassification.Proven].Select(item => item.Target.Symbol!).Order(StringComparer.Ordinal));
        Assert.Equal(["GET /e"], byClass[ViolationClassification.RuntimeValidationRequired].Select(item => item.Target.Symbol!));
        Assert.Empty(byClass[ViolationClassification.Potential]);
    }

    /// <summary>ASP003 proves direct and transient captive dependencies, flags mixed lifetimes and factories, and accepts the rest.</summary>
    [Fact]
    public async Task Lifetimes_classify_proven_potential_and_factory_registrations()
    {
        var violations = await EvaluateAsync(await BuildAsync(), "ASP003");
        string[] Targets(ViolationClassification value) => violations.Where(item => item.Classification == value)
            .Select(item => item.Target.Symbol!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["Web.IHandlerHost", "Web.ISingletonThing", "Web.IViaTransient"], Targets(ViolationClassification.Proven));
        Assert.Equal(["Web.IHolder", "Web.ITwoCtor"], Targets(ViolationClassification.Potential));
        Assert.Equal(["Web.IFactoryThing"], Targets(ViolationClassification.RuntimeValidationRequired));
        Assert.Contains("Web.IViaTransient → Web.IMiddle → Web.IScopedThing", violations.Single(item => item.Target.Symbol == "Web.IViaTransient").Message);
    }

    /// <summary>ASP005 reports concrete injected types from other projects, not abstractions or unattributed parameters.</summary>
    [Fact]
    public async Task Application_abstractions_are_required_for_injected_services()
    {
        var violations = await EvaluateAsync(await BuildAsync(), "ASP005");
        Assert.Equal(["GET /svc", "Web.ConcreteController"], violations.Select(item => item.Target.Symbol!).Order(StringComparer.Ordinal));
        Assert.All(violations, item => Assert.Contains("Application.ConcreteOrderService", item.Message));
    }

    /// <summary>Selecting only a non-ASP rule skips fact extraction, while selecting an ASP rule without facts is incomplete.</summary>
    [Fact]
    public async Task Facts_are_extracted_only_for_selected_asp_rules_and_missing_facts_are_incomplete()
    {
        var context = await BuildAsync(["AG001"]);
        Assert.Null(context.AspNetCore);
        var result = await new ControllerPersistenceRule().EvaluateAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Equal("aspnetcore-facts-unavailable", Assert.Single(result.Issues).Code);
    }

    /// <summary>Findings are identical across repeated runs, so baselines and reports stay reproducible.</summary>
    [Fact]
    public async Task Findings_are_deterministic()
    {
        var first = await EvaluateAsync(await BuildAsync(), "ASP003");
        var second = await EvaluateAsync(await BuildAsync(), "ASP003");
        Assert.Equal(first.Select(item => item.Id), second.Select(item => item.Id));
    }
}
