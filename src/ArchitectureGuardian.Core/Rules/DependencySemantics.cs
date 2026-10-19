namespace ArchitectureGuardian.Core.Rules;

/// <summary>Shares compiler-compatible dependency decisions and metadata between the two engines.</summary>
internal static class DependencySemantics
{
    internal const string ModuleId = "AG001";
    internal const string ModuleTitle = "Module dependencies";
    internal const string ModuleAnchor = "ag001-module-dependencies";
    internal const string LayerId = "AG002";
    internal const string LayerTitle = "Layer dependencies";
    internal const string LayerAnchor = "ag002-layer-dependencies";
    internal const string ImplementationId = "AG004";
    internal const string ImplementationTitle = "Implementation boundaries";
    internal const string ImplementationAnchor = "ag004-implementation-boundaries";
    internal const string DocumentationRoot = "https://github.com/kbourzayq/arch-guard/blob/main/docs/rule-catalog.md#";

    internal static bool AllowsModule(string source, string target, string? contract, IEnumerable<string> permissions) =>
        source == target || permissions.Contains("module:" + target, StringComparer.Ordinal)
        || contract is not null && permissions.Contains("contract:" + target + "/" + contract, StringComparer.Ordinal);

    internal static bool AllowsImplementation(string source, string target, string? contract) =>
        source == target || contract is not null;

    internal static bool AllowsLayer(string source, string target, IEnumerable<string> permissions) =>
        source == target || permissions.Contains(target, StringComparer.Ordinal);
}
