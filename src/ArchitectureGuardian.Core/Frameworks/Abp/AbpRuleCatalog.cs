using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.Abp;

/// <summary>Publishes the ABP001–ABP005 identities, their confidence, static support and runtime blind spots.</summary>
/// <remarks>The catalog is separate from the AG001–AG006 core catalog because these rules need ABP Framework facts.</remarks>
public static class AbpRuleCatalog
{
    private const string Root = "https://github.com/kbourzayq/arch-guard/blob/main/docs/rule-catalog.md#";

    /// <summary>Gets ABP001, which checks that module [DependsOn] declarations cover referenced solution modules.</summary>
    public static RuleDescriptor ModuleDependencies { get; } = Create("ABP001", "Module dependency declarations", "abp001-module-dependency-declarations");

    /// <summary>Gets ABP002, which checks that application services exchange DTOs rather than entities.</summary>
    public static RuleDescriptor ApplicationServiceDtos { get; } = Create("ABP002", "Application service DTO boundaries", "abp002-application-service-dto-boundaries");

    /// <summary>Gets ABP003, which checks that repositories are used only in the declared repository layers.</summary>
    public static RuleDescriptor RepositoryLayers { get; } = Create("ABP003", "Repository access layers", "abp003-repository-access-layers");

    /// <summary>Gets ABP004, which checks that remotely exposable application service methods declare authorization.</summary>
    public static RuleDescriptor ApplicationServiceAuthorization { get; } = Create("ABP004", "Application service authorization", "abp004-application-service-authorization");

    /// <summary>Gets ABP005, which checks source for code that leaves the current tenant's data isolation.</summary>
    public static RuleDescriptor TenantIsolation { get; } = Create("ABP005", "Tenant isolation bypass", "abp005-tenant-isolation-bypass");

    /// <summary>Gets the read-only ABP rule catalog in ABP001–ABP005 order.</summary>
    public static IReadOnlyList<RuleDescriptor> All { get; } = Array.AsReadOnly<RuleDescriptor>(
        [ModuleDependencies, ApplicationServiceDtos, RepositoryLayers, ApplicationServiceAuthorization, TenantIsolation]);

    /// <summary>Gets the ABP Framework versions whose symbols the rules were verified against.</summary>
    public static string VerifiedVersions => "10.0.0 and 10.7.1";

    /// <summary>Gets the confidence, supported static cases and runtime blind spots for each rule, in rule order.</summary>
    public static IReadOnlyList<AbpRuleSupport> Support { get; } = Array.AsReadOnly<AbpRuleSupport>([
        new("ABP001", "Proven: a missing declaration is visible in source.",
            ["A project whose AbpModule class does not reach, through its transitive [DependsOn] closure, a module declared by a referenced solution project; a reference that another referenced module project also has is checked at that project."],
            ["Package and assembly references are not checked; only direct solution project references are.", "Modules added at runtime, such as plug-in modules or PreConfigure-time additions, are not seen.", "Projects without an AbpModule class are skipped."]),
        new("ABP002", "Proven: the entity type is part of the declared signature.",
            ["Public methods of classes and interfaces implementing IApplicationService whose parameters or return types are entities (IEntity), including Task, collection and PagedResultDto wrappers, generic arguments and arrays.", "Public properties of source DTO types reached from those signatures, up to four levels deep."],
            ["Objects returned as object, dynamic or through an untyped serializer are not seen.", "Entities in inherited ABP base-class signatures, such as CrudAppService type arguments, are not checked."]),
        new("ABP003", "Proven: the repository type is used in a layer outside abp.repositoryLayers.",
            ["Bound uses of types implementing Volo.Abp.Domain.Repositories.IRepository, including generic repositories, custom repository interfaces and their implementations, in projects outside abp.repositoryLayers."],
            ["Repositories resolved through IServiceProvider, LazyServiceProvider or reflection are not seen.", "Data reached through another abstraction, such as a domain service, is allowed."]),
        new("ABP004", "Potential: a missing declaration is a security concern, not proof of an unprotected endpoint.",
            ["Public instance methods of public, non-generic, concrete classes implementing IApplicationService, including methods inherited from source base classes and ABP CRUD base classes; each overload is identified by its parameter types.", "[Authorize] and [AllowAnonymous] on the method, a method it overrides, its class or a base class; AuthorizationService.CheckAsync, IsGrantedAsync, AuthorizeAsync and CheckPolicyAsync calls in the method body; CRUD policy names such as GetListPolicyName assigned in source, where a constant null or empty name supplies no policy.", "[RemoteService(false)] services and [IntegrationService] services, including those marked through a base class or interface, are skipped because they are not exposed as public HTTP endpoints by default."],
            ["Whether conventional controllers expose the service, and the effective route, are runtime configuration.", "Global authorization policies, authorization middleware and permission definitions are not evaluated.", "Attributes on interfaces are not counted, because ABP's authorization interceptor reads the implementation.", "Checks in called helper methods are not followed."]),
        new("ABP005", "Potential: the call is policy evidence for review and does not prove that another tenant's data is exposed.",
            ["DataFilter.Disable<IMultiTenant>() and IDataFilter<IMultiTenant>.Disable() calls.", "ICurrentTenant.Change calls whose tenant ID argument is null, matched by parameter so named arguments in any order are supported."],
            ["Which data the code reads, and whether it returns it to a caller, depends on runtime data and is not evaluated.", "Filters disabled through configuration, such as AbpDataFilterOptions, are not seen.", "Changing to a non-null tenant ID is not reported."]),
    ]);

    private static RuleDescriptor Create(string id, string title, string anchor) => new(id, title, new Uri(Root + anchor));
}
