using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.EntityFrameworkCore;

/// <summary>Publishes the EF001–EF003 identities and their documented static support and runtime blind spots.</summary>
/// <remarks>The catalog is separate from the AG001–AG006 core catalog because these rules need EF Core facts.</remarks>
public static class EntityFrameworkCoreRuleCatalog
{
    private const string Root = "https://github.com/kbourzayq/arch-guard/blob/main/docs/rule-catalog.md#";

    /// <summary>Gets EF001, which checks that a DbContext is used only by the module that owns it.</summary>
    public static RuleDescriptor DbContextOwnership { get; } = Create("EF001", "DbContext ownership", "ef001-dbcontext-ownership");

    /// <summary>Gets EF002, which checks that EF Core is used only in the declared persistence layers.</summary>
    public static RuleDescriptor PersistenceLayers { get; } = Create("EF002", "Persistence layers", "ef002-persistence-layers");

    /// <summary>Gets EF003, which checks source for references to forbidden EF Core APIs.</summary>
    public static RuleDescriptor ForbiddenApis { get; } = Create("EF003", "Forbidden persistence APIs", "ef003-forbidden-persistence-apis");

    /// <summary>Gets the read-only EF rule catalog in EF001–EF003 order.</summary>
    public static IReadOnlyList<RuleDescriptor> All { get; } = Array.AsReadOnly<RuleDescriptor>([DbContextOwnership, PersistenceLayers, ForbiddenApis]);

    /// <summary>Gets the supported static cases and runtime blind spots for each rule, in rule order.</summary>
    public static IReadOnlyList<EntityFrameworkCoreRuleSupport> Support { get; } = Array.AsReadOnly<EntityFrameworkCoreRuleSupport>([
        new("EF001",
            ["Bound uses of a DbContext subclass declared in a source project owned by another module, including method return types, indexers and generic arguments such as IDbContextFactory<TContext>, also behind aliases.", "Uses of the DbContext base type outside DbContext subclasses, reported as runtime-validation requirements."],
            ["DbContext types from package or assembly references have no module owner and are not reported; projects that produce the same assembly identity make ownership ambiguous, and EF001 then reports incomplete coverage.", "Which context the container supplies for the DbContext base type is not evaluated.", "Data reached through another module's repository contract is allowed; declared contracts are checked by AG001 and AG004."]),
        new("EF002",
            ["Bound uses of types from, derived from or implementing types from the Microsoft.EntityFrameworkCore namespace, including DbContext subclasses and EF Core extension methods, in projects outside entityFrameworkCore.persistenceLayers."],
            ["Type arguments of AddDbContext, AddDbContextPool, AddDbContextFactory and AddPooledDbContextFactory, and EF Core option configuration in their callbacks, are composition and are not reported; contexts used inside callbacks are.", "Persistence reached only through a non-EF abstraction, such as a repository interface, is not followed.", "Types resolved with reflection or dynamic are not seen."]),
        new("EF003",
            ["References to EF Core methods named in entityFrameworkCore.forbiddenApis, including extension methods, method groups and EF Core methods declared in other namespaces such as AddDbContext.", "IgnoreQueryFilters is reported as a potential concern: it is policy evidence and does not prove tenant-data exposure."],
            ["Global query filter contents and the data a query returns are not evaluated; confirm tenant isolation with tests.", "SQL executed through ADO.NET or other libraries is not EF Core and is not reported."]),
    ]);

    private static RuleDescriptor Create(string id, string title, string anchor) => new(id, title, new Uri(Root + anchor));
}
