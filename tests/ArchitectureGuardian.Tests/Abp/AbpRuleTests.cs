using ArchitectureGuardian.Analysis.Abp;
using ArchitectureGuardian.Analysis.Dependencies;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Frameworks.Abp;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Rules;
using ArchitectureGuardian.Tests.Workspaces;
using Xunit;

namespace ArchitectureGuardian.Tests.Abp;

/// <summary>Evaluates the abp pack and ABP001–ABP005 against a real ABP Framework solution with a compliant and a violating module.</summary>
/// <param name="fixture">Restored ABP workspace.</param>
[Trait("Category", "Integration")]
public sealed class AbpRuleTests(AbpWorkspaceFixture fixture) : IClassFixture<AbpWorkspaceFixture>
{
    private string Root => Path.Combine(fixture.Root, "Abp");

    private async Task<ArchitecturePolicy> PolicyAsync(string? replace = null, string? with = null)
    {
        var path = Path.Combine(Root, "architecture.guardian.yaml");
        if (replace is null)
        {
            return await new YamlPolicyLoader().LoadAsync(path, TestContext.Current.CancellationToken);
        }

        var yaml = (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Replace(replace, with, StringComparison.Ordinal);
        Assert.NotEqual(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), yaml);
        return new YamlPolicyLoader().Parse(yaml, path, TestContext.Current.CancellationToken);
    }

    private async Task<DependencyBuildResult> BuildAsync(ArchitecturePolicy policy)
    {
        using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(Root, "BookStore.slnx"), isTrusted: true),
            TestContext.Current.CancellationToken);
        Assert.True(workspace.Status == AnalysisStatus.Completed, string.Join('\n', workspace.Issues.Select(item => item.Code + ": " + item.Message)));
        var context = await new DependencyGraphBuilder().BuildAsync(workspace, Root, policy, TestContext.Current.CancellationToken);
        Assert.True(context.Status == AnalysisStatus.Completed, string.Join('\n', context.Issues.Select(item => item.Code + ": " + item.Message)));
        return context;
    }

    private static async Task<IReadOnlyList<RuleViolation>> EvaluateAsync(DependencyBuildResult context, string id)
    {
        var result = await SolutionRules.All.Single(rule => rule.Descriptor.Id == id).EvaluateAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisStatus.Completed, result.Status);
        return result.Violations;
    }

    /// <summary>Gets the offending symbol each finding records at the end of its context path.</summary>
    private static string[] Targets(IEnumerable<RuleViolation> violations, ViolationClassification classification) => violations
        .Where(item => item.Classification == classification).Select(item => item.Context!.DependencyPath[^1].Identity).Order(StringComparer.Ordinal).ToArray();

    /// <summary>A full pack scan keeps the compliant Catalog module clean and reports exactly the seeded violations.</summary>
    [Fact]
    public async Task Pack_scan_keeps_the_compliant_module_clean()
    {
        var policy = await PolicyAsync();
        var result = await new AnalysisService<DependencyBuildResult>(policy.SelectRules(SolutionRules.All))
            .AnalyzeAsync(new(await BuildAsync(policy), policy.SeverityPolicy), TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisStatus.Completed, result.Status);
        Assert.DoesNotContain(result.Diagnostics, item => item.Violation.Target.Module == "Catalog");
        // Layering, technology restrictions and module permissions hold for the whole solution; only ABP conventions are seeded.
        Assert.DoesNotContain(result.Diagnostics, item => item.Violation.Rule.Id.StartsWith("AG", StringComparison.Ordinal));
        Assert.Equal(["ABP001", "ABP002", "ABP003", "ABP004", "ABP004", "ABP004", "ABP004", "ABP005", "ABP005"],
            result.Diagnostics.Select(item => item.Violation.Rule.Id).Order(StringComparer.Ordinal));
    }

    /// <summary>ABP001 proves a missing [DependsOn] once, at the module that references the project directly.</summary>
    [Fact]
    public async Task Module_dependency_declarations_follow_direct_references()
    {
        var violation = Assert.Single(await EvaluateAsync(await BuildAsync(await PolicyAsync()), "ABP001"));
        Assert.Equal(ViolationClassification.Proven, violation.Classification);
        Assert.Equal("Ordering", violation.Target.Module);
        Assert.Equal("Acme.Ordering.OrderingApplicationModule", violation.Target.Symbol);
        // Catalog.Domain.Shared is referenced only transitively, through Catalog.Application.Contracts, so it is not reported again.
        Assert.Contains("Acme.Catalog.Application.Contracts.csproj", violation.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Domain.Shared", violation.Message, StringComparison.Ordinal);
        Assert.Equal(["Acme.Catalog.CatalogApplicationContractsModule"], Assert.Single(violation.Context!.PolicyConstraints).Values);
        Assert.Equal("src/Ordering/Acme.Ordering.Application/OrderingApplicationModule.cs", violation.Location!.Path);
        Assert.Contains("[DependsOn(typeof(CatalogApplicationContractsModule))]", violation.Remediation, StringComparison.Ordinal);
    }

    /// <summary>ABP002 proves entities in application service signatures, including through DTO properties.</summary>
    [Fact]
    public async Task Application_services_expose_dtos_not_entities()
    {
        var violation = Assert.Single(await EvaluateAsync(await BuildAsync(await PolicyAsync()), "ABP002"));
        Assert.Equal(ViolationClassification.Proven, violation.Classification);
        Assert.Equal("Acme.Ordering.OrderAppService", violation.Target.Symbol);
        Assert.Equal("Acme.Ordering.Order", violation.Context!.DependencyPath[1].Identity);
        // OrderSourceDto is met at the depth limit through Deep first; the shorter Direct path must still inspect it.
        Assert.Equal(["'GetAsync' exposes entity 'Acme.Ordering.Order'.",
            "'GetReportAsync → OrderReportDto.Direct → OrderSourceDto.Source' exposes entity 'Acme.Ordering.Order'.",
            "'GetSummaryAsync → OrderSummaryDto.Source' exposes entity 'Acme.Ordering.Order'."],
            violation.Evidence.Select(item => item.Description).Order(StringComparer.Ordinal));
    }

    /// <summary>ABP003 proves repository use outside the repository layers; Domain, Application and Persistence pass.</summary>
    [Fact]
    public async Task Repositories_stay_in_the_repository_layers()
    {
        var context = await BuildAsync(await PolicyAsync());
        var violation = Assert.Single(await EvaluateAsync(context, "ABP003"));
        Assert.Equal(ViolationClassification.Proven, violation.Classification);
        Assert.Equal(("Host", "Acme.BookStore.Web.BookCatalogController"), (violation.Target.Module, violation.Target.Symbol));
        Assert.Equal("Volo.Abp.Domain.Repositories.IRepository<Acme.Catalog.Book, System.Guid>", violation.Context!.DependencyPath[1].Identity);
        // Custom repository interfaces and their EF Core implementations are repositories too, used in allowed layers.
        Assert.Contains(context.Abp!.RepositoryUses, item => item.Repository == "Acme.Catalog.IBookRepository" && item.Project.Contains("EntityFrameworkCore", StringComparison.Ordinal));

        // Scoping the convention to include the host removes the finding.
        var scoped = await BuildAsync(await PolicyAsync("exclude: [layer:Tests]", "exclude: [layer:Tests]\nabp:\n  repositoryLayers: [Application, Domain, Host, Persistence]"));
        Assert.Empty(await EvaluateAsync(scoped, "ABP003"));
    }

    /// <summary>ABP004 reports exposable methods without declarations as potential; attributes, checks and policy names pass.</summary>
    [Fact]
    public async Task Application_service_authorization_is_a_potential_finding()
    {
        var violations = await EvaluateAsync(await BuildAsync(await PolicyAsync()), "ABP004");
        // Overloads are separate endpoints; null CRUD policy names supply no authorization, a non-null override does.
        Assert.Equal(["Acme.Ordering.OrderAppService.CancelAsync(System.Guid)", "Acme.Ordering.OrderAppService.CancelAsync(string)",
            "Acme.Ordering.OrderCrudAppService.CreateAsync(Acme.Ordering.OrderDto)", "Acme.Ordering.OrderCrudAppService.DeleteAsync(System.Guid)"],
            Targets(violations, ViolationClassification.Potential));
        Assert.Empty(Targets(violations, ViolationClassification.Proven));
        Assert.All(violations, item => Assert.Contains("potential concern", item.Message, StringComparison.Ordinal));
        var inherited = violations.Single(item => item.Context!.DependencyPath[^1].Identity == "Acme.Ordering.OrderCrudAppService.CreateAsync(Acme.Ordering.OrderDto)");
        Assert.Contains("inherited from an ABP base class", inherited.Message, StringComparison.Ordinal);
        Assert.Contains("PolicyName", inherited.Remediation, StringComparison.Ordinal);
    }

    /// <summary>ABP004 facts classify every declaration kind, and services that ABP does not expose remotely are absent.</summary>
    [Fact]
    public async Task Authorization_declarations_are_classified()
    {
        var methods = (await BuildAsync(await PolicyAsync())).Abp!.ServiceMethods.ToDictionary(item => item.Service + "." + item.Method, item => item.Authorization);
        Assert.Equal(AbpAuthorizationDeclaration.Authorize, methods["Acme.Ordering.OrderAppService.GetAsync(System.Guid)"]);
        Assert.Equal(AbpAuthorizationDeclaration.PermissionCheck, methods["Acme.Ordering.OrderAppService.PlaceAsync(System.Guid)"]);
        Assert.Equal(AbpAuthorizationDeclaration.AllowAnonymous, methods["Acme.Ordering.OrderAppService.IsOpenAsync()"]);
        Assert.Equal(AbpAuthorizationDeclaration.PolicyName, methods["Acme.Ordering.OrderCrudAppService.GetListAsync(Volo.Abp.Application.Dtos.PagedAndSortedResultRequestDto)"]);
        Assert.Equal(AbpAuthorizationDeclaration.PolicyName, methods["Acme.Ordering.OrderCrudAppService.UpdateAsync(System.Guid, Acme.Ordering.OrderDto)"]);
        Assert.Equal(AbpAuthorizationDeclaration.Authorize, methods["Acme.Catalog.BookAppService.GetListAsync(Volo.Abp.Application.Dtos.PagedAndSortedResultRequestDto)"]);
        // A source base class in another project is bound with its own compilation, and overrides inherit [Authorize].
        Assert.Equal(AbpAuthorizationDeclaration.PermissionCheck, methods["Acme.BookStore.Web.StorefrontAppService.CountAsync()"]);
        Assert.Equal(AbpAuthorizationDeclaration.Authorize, methods["Acme.BookStore.Web.StorefrontAppService.DescribeAsync()"]);
        Assert.DoesNotContain("Acme.Ordering.OrderAppService.RecalculateAsync()", methods.Keys);
        // RemoteService(false), integration services (also inherited), internal and generic services are not published.
        Assert.DoesNotContain(methods.Keys, key => new[] { "OrderNumberAppService", "OrderSyncAppService", "OrderBackfillAppService",
            "InternalOrderAppService", "GenericOrderAppService" }.Any(service => key.StartsWith("Acme.Ordering." + service, StringComparison.Ordinal)));
    }

    /// <summary>ABP005 reports tenant-isolation bypasses as potential; seeding a known tenant passes and bypass layers are honored.</summary>
    [Fact]
    public async Task Tenant_isolation_bypasses_are_potential_findings()
    {
        var violations = await EvaluateAsync(await BuildAsync(await PolicyAsync()), "ABP005");
        Assert.Equal(["ICurrentTenant.Change(null)", "IDataFilter.Disable<IMultiTenant>"], Targets(violations, ViolationClassification.Potential));
        Assert.All(violations, item => Assert.Equal("Acme.Ordering.OrderReportAppService", item.Target.Symbol));
        Assert.All(violations, item => Assert.Contains("does not prove", item.Message, StringComparison.Ordinal));
        // The tenant ID is matched by parameter: Change(name: "host", id: null) is a host switch, while the seeder's
        // Change(name: null, id: tenantId) is not.
        var host = violations.Single(item => item.Context!.DependencyPath[^1].Identity == "ICurrentTenant.Change(null)");
        Assert.Equal(2, host.Evidence.Count);

        var allowed = await BuildAsync(await PolicyAsync("exclude: [layer:Tests]", "exclude: [layer:Tests]\nabp:\n  tenantFilterBypassLayers: [Application]"));
        Assert.Empty(await EvaluateAsync(allowed, "ABP005"));
    }

    /// <summary>A selected ABP rule without extracted facts is incomplete, never a clean result.</summary>
    [Fact]
    public async Task Missing_facts_never_produce_a_clean_result()
    {
        var context = await BuildAsync(await PolicyAsync());
        var withoutFacts = new DependencyBuildResult(context.Repository, context.Graph, context.Status, context.Issues);
        foreach (var rule in SolutionRules.All.Where(item => item.Descriptor.Id.StartsWith("ABP", StringComparison.Ordinal)))
        {
            var result = await rule.EvaluateAsync(withoutFacts, TestContext.Current.CancellationToken);
            Assert.Equal(AnalysisStatus.Incomplete, result.Status);
            Assert.Contains(result.Issues, issue => issue.Code == "abp-facts-unavailable" && issue.RuleId == rule.Descriptor.Id);
            Assert.Empty(result.Violations);
        }
    }

    /// <summary>Extraction is deterministic for the same snapshot, and cancellation is surfaced rather than reducing coverage.</summary>
    [Fact]
    public async Task Extraction_is_deterministic_and_cancellable()
    {
        var policy = await PolicyAsync();
        var first = (await BuildAsync(policy)).Abp!;
        var second = (await BuildAsync(policy)).Abp!;
        Assert.Equal(first.Modules.Select(item => item.Module + string.Join(",", item.DependsOn)), second.Modules.Select(item => item.Module + string.Join(",", item.DependsOn)));
        Assert.Equal(first.ServiceMethods, second.ServiceMethods);
        Assert.Equal(first.EntityExposures, second.EntityExposures);
        Assert.Equal(first.RepositoryUses, second.RepositoryUses);
        Assert.Equal(first.TenantBypasses, second.TenantBypasses);

        using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(new(Path.Combine(Root, "BookStore.slnx"), isTrusted: true),
            TestContext.Current.CancellationToken);
        var context = await new DependencyGraphBuilder().BuildAsync(workspace, Root, policy, TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AbpFactExtractor.ExtractAsync(workspace.Solution!, context.Repository!.Projects, Root, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SolutionRules.All.Single(rule => rule.Descriptor.Id == "ABP001").EvaluateAsync(context, cancellation.Token));
    }
}
