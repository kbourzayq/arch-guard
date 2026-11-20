using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.AspNetCore;

/// <summary>Publishes the ASP001–ASP005 identities and their documented static support and runtime blind spots.</summary>
/// <remarks>The catalog is separate from the AG001–AG006 core catalog because these rules need ASP.NET Core facts.</remarks>
public static class AspNetCoreRuleCatalog
{
    private const string Root = "https://github.com/kbourzayq/arch-guard/blob/main/docs/rule-catalog.md#";

    /// <summary>Gets ASP001, which checks controllers for direct persistence use.</summary>
    public static RuleDescriptor ControllerPersistence { get; } = Create("ASP001", "Controller persistence access", "asp001-controller-persistence-access");

    /// <summary>Gets ASP002, which checks that endpoints declare authorization.</summary>
    public static RuleDescriptor DeclaredAuthorization { get; } = Create("ASP002", "Declared endpoint authorization", "asp002-declared-endpoint-authorization");

    /// <summary>Gets ASP003, which checks service lifetimes for captive dependencies.</summary>
    public static RuleDescriptor ServiceLifetimes { get; } = Create("ASP003", "Service lifetime mismatches", "asp003-service-lifetime-mismatches");

    /// <summary>Gets ASP004, which checks minimal API endpoints for direct persistence use.</summary>
    public static RuleDescriptor ApiPersistence { get; } = Create("ASP004", "Minimal API persistence access", "asp004-minimal-api-persistence-access");

    /// <summary>Gets ASP005, which checks that endpoints depend on application abstractions.</summary>
    public static RuleDescriptor ApplicationAbstractions { get; } = Create("ASP005", "Application abstraction dependencies", "asp005-application-abstraction-dependencies");

    /// <summary>Gets the read-only ASP rule catalog in ASP001–ASP005 order.</summary>
    public static IReadOnlyList<RuleDescriptor> All { get; } = Array.AsReadOnly<RuleDescriptor>(
        [ControllerPersistence, DeclaredAuthorization, ServiceLifetimes, ApiPersistence, ApplicationAbstractions]);

    /// <summary>Gets the supported static cases and runtime blind spots for each rule, in rule order.</summary>
    public static IReadOnlyList<AspNetCoreRuleSupport> Support { get; } = Array.AsReadOnly<AspNetCoreRuleSupport>([
        new("ASP001",
            ["Controller types that derive from ControllerBase: constructor, field, property, parameter, return (including generic arguments such as Task<T>) and body uses of types from, or derived from, persistence namespaces."],
            ["Persistence reached only through a non-persistence abstraction is not followed.", "Types resolved with reflection or dynamic are not seen."]),
        new("ASP002",
            ["[Authorize] and [AllowAnonymous] on a controller, its base classes or an action.", "RequireAuthorization and AllowAnonymous on a minimal API call chain, on a MapGroup chain, or on a local variable initialized with one."],
            ["A fallback authorization policy or authorization middleware is not inspected.", "Route groups received as a parameter, field or return value are reported as runtime-validation requirements.", "The content of an authorization policy is not evaluated."]),
        new("ASP003",
            ["AddSingleton, AddScoped, AddTransient and their TryAdd forms with generic type arguments or typeof arguments.", "AddDbContext and AddDbContextPool registered with the default scoped lifetime.", "A singleton that depends, directly or through transient services, on a scoped service."],
            ["Factory registrations are reported as runtime-validation requirements for singletons.", "Separate containers are not distinguished; a capture that relies on a registration in another project is reported as potential.", "Assembly scanning, keyed services, conditional registration and registrations in other assemblies are not seen.", "More than one public constructor, or a service registered with several lifetimes, yields a potential finding.", "Enable ValidateScopes and ValidateOnBuild to confirm the container's behavior."]),
        new("ASP004",
            ["Map, MapGet, MapPost, MapPut, MapDelete, MapPatch and MapMethods handlers written as lambdas or method groups: parameters and body uses of persistence types."],
            ["Handlers supplied through a variable of delegate type are not followed.", "Persistence reached only through a non-persistence abstraction is not followed."]),
        new("ASP005",
            ["Types injected into controller constructors, [FromServices] parameters and [FromServices] minimal API parameters that are concrete classes declared in another source project."],
            ["Which concrete type the container supplies for an abstraction is not evaluated.", "Unattributed minimal API class parameters are not treated as injected because binding is inferred at runtime."]),
    ]);

    private static RuleDescriptor Create(string id, string title, string anchor) => new(id, title, new Uri(Root + anchor));
}
