namespace ArchitectureGuardian.Core.Rules;

/// <summary>Publishes the canonical AG001–AG006 identities shared by all analysis engines and hosts.</summary>
/// <remarks>The catalog supplies metadata; the corresponding evaluation implementations are separate features.</remarks>
public static class RuleCatalog
{
    private const string DocumentationRoot = "https://github.com/kbourzayq/arch-guard/blob/main/docs/rule-catalog.md#";

    /// <summary>Gets AG001, which checks policy-forbidden dependencies between logical modules.</summary>
    public static RuleDescriptor ModuleDependencies { get; } = Create("AG001", "Module dependencies", "ag001-module-dependencies");

    /// <summary>Gets AG002, which checks policy-forbidden dependencies between architectural layers.</summary>
    public static RuleDescriptor LayerDependencies { get; } = Create("AG002", "Layer dependencies", "ag002-layer-dependencies");

    /// <summary>Gets AG003, which checks cycles in the solution-wide logical module graph.</summary>
    public static RuleDescriptor ModuleCycles { get; } = Create("AG003", "Module cycles", "ag003-module-cycles");

    /// <summary>Gets AG004, which checks access across module implementation boundaries.</summary>
    public static RuleDescriptor ImplementationBoundaries { get; } = Create("AG004", "Implementation boundaries", "ag004-implementation-boundaries");

    /// <summary>Gets AG005, which checks assembly or namespace references forbidden by policy.</summary>
    public static RuleDescriptor ForbiddenReferences { get; } = Create("AG005", "Forbidden assembly/namespace references", "ag005-forbidden-references");

    /// <summary>Gets AG006, which checks domain references to infrastructure technology forbidden by policy.</summary>
    public static RuleDescriptor DomainInfrastructure { get; } = Create("AG006", "Domain infrastructure references", "ag006-domain-infrastructure");

    /// <summary>Gets the read-only canonical catalog in AG001–AG006 order.</summary>
    public static IReadOnlyList<RuleDescriptor> All { get; } = Array.AsReadOnly<RuleDescriptor>(
        [ModuleDependencies, LayerDependencies, ModuleCycles, ImplementationBoundaries, ForbiddenReferences, DomainInfrastructure]);

    private static RuleDescriptor Create(string id, string title, string anchor) => new(id, title, new Uri(DocumentationRoot + anchor));
}
