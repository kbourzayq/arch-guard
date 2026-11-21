using System.Collections.ObjectModel;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Core.Discovery;

/// <summary>Recognizes ABP Framework layered solutions and mirrors the built-in abp pack's layers for review-only proposals.</summary>
/// <remarks>
/// Core cannot read the embedded pack, so the layer template is restated here. A test composes the pack and compares it
/// with this template, and the YAML writer verifies the composed proposal against the inventory before using extends.
/// </remarks>
public static class AbpSolutionTemplate
{
    /// <summary>Gets the built-in pack identity that ABP proposals extend.</summary>
    public const string PackId = "abp";

    /// <summary>Gets the pinned pack version that ABP proposals extend.</summary>
    public const string PackVersion = "1.0.0";

    private static readonly IReadOnlyList<string> ProviderNamespaces =
        Array.AsReadOnly(["Microsoft.EntityFrameworkCore", "MongoDB", "Volo.Abp.EntityFrameworkCore", "Volo.Abp.MongoDB"]);
    private static readonly IReadOnlyList<string> CoreNamespaces =
        Array.AsReadOnly(["Microsoft.AspNetCore.Mvc", .. ProviderNamespaces, "Volo.Abp.AspNetCore.Mvc"]);
    private static readonly IReadOnlyList<string> None = Array.AsReadOnly(Array.Empty<string>());

    /// <summary>Gets the pack layers in declaration order, with their permitted dependencies and technology restrictions.</summary>
    public static IReadOnlyList<(string Id, IReadOnlyList<string> AllowedDependencies, IReadOnlyList<string> ForbiddenNamespaces)> Layers { get; } =
        Array.AsReadOnly<(string, IReadOnlyList<string>, IReadOnlyList<string>)>([
            ("DomainShared", None, CoreNamespaces),
            ("Domain", Layer("DomainShared"), CoreNamespaces),
            ("ApplicationContracts", Layer("DomainShared"), CoreNamespaces),
            ("Application", Layer("ApplicationContracts", "Domain", "DomainShared"), CoreNamespaces),
            ("Persistence", Layer("Domain", "DomainShared"), None),
            ("HttpApi", Layer("ApplicationContracts", "DomainShared"), ProviderNamespaces),
            ("HttpApiClient", Layer("ApplicationContracts", "DomainShared"), ProviderNamespaces),
            ("Host", Layer("Application", "ApplicationContracts", "Domain", "DomainShared", "HttpApi", "HttpApiClient", "Persistence"), None),
            ("Tests", Layer("Application", "ApplicationContracts", "Domain", "DomainShared", "Host", "HttpApi", "HttpApiClient", "Persistence"), None),
        ]);

    /// <summary>Gets the rules the pack selects and their default severities.</summary>
    public static IReadOnlyDictionary<string, DiagnosticSeverity> Rules { get; } = new ReadOnlyDictionary<string, DiagnosticSeverity>(new Dictionary<string, DiagnosticSeverity>(StringComparer.Ordinal)
    {
        ["AG002"] = DiagnosticSeverity.Error,
        ["AG006"] = DiagnosticSeverity.Error,
        ["ABP001"] = DiagnosticSeverity.Error,
        ["ABP002"] = DiagnosticSeverity.Error,
        ["ABP003"] = DiagnosticSeverity.Error,
        ["ABP004"] = DiagnosticSeverity.Warning,
        ["ABP005"] = DiagnosticSeverity.Warning,
    });

    /// <summary>Gets the layers whose projects may use ABP repositories.</summary>
    public static IReadOnlyList<string> RepositoryLayers { get; } = Array.AsReadOnly(["Application", "Domain", "Persistence", "Tests"]);

    /// <summary>Project-name suffixes of the ABP startup template, longest first so Domain.Shared wins over Domain.</summary>
    private static readonly (string Suffix, string Layer)[] Suffixes =
    [
        (".Application.Contracts", "ApplicationContracts"),
        (".EntityFrameworkCore", "Persistence"),
        (".HttpApi.Client", "HttpApiClient"),
        (".Domain.Shared", "DomainShared"),
        (".Application", "Application"),
        (".HttpApi", "HttpApi"),
        (".MongoDB", "Persistence"),
        (".Domain", "Domain"),
    ];

    /// <summary>Name segments of ABP composition roots, such as Acme.BookStore.Web or Acme.BookStore.HttpApi.Host.</summary>
    private static readonly string[] HostSegments = ["AuthServer", "Blazor", "DbMigrator", "Host", "Web"];

    /// <summary>Name segments that begin an ABP layer or role, separating the module prefix from the layer.</summary>
    private static readonly string[] RoleSegments =
        ["Application", "AuthServer", "Blazor", "DbMigrator", "Domain", "EntityFrameworkCore", "Host", "HttpApi", "MongoDB", "TestBase", "Web"];

    /// <summary>Classifies a project by the ABP startup template's naming convention.</summary>
    /// <param name="name">The project file name without its extension, such as Acme.BookStore.Domain.Shared.</param>
    /// <returns>The candidate module prefix and pack layer, or null when the name follows no ABP convention.</returns>
    public static (string Module, string Layer)? Classify(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var segments = name.Split('.');
        var start = Array.FindIndex(segments, segment => RoleSegments.Contains(segment, StringComparer.Ordinal) || IsTest(segment));
        // Test projects take the test role first, so Acme.BookStore.Domain.Tests is a test, not a domain project.
        if (segments.Any(IsTest))
        {
            return start > 0 ? (string.Join('.', segments.Take(start)), "Tests") : null;
        }

        // An exact template suffix is the strongest signal and keeps role words inside the module prefix intact.
        foreach (var (suffix, layer) in Suffixes)
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
            {
                return (name.Substring(0, name.Length - suffix.Length), layer);
            }
        }

        return start > 0 && segments.Skip(start).Any(segment => HostSegments.Contains(segment, StringComparer.Ordinal))
            ? (string.Join('.', segments.Take(start)), "Host") : null;
    }

    /// <summary>Builds the candidate policy equivalent to extending the pack, using exact project selectors.</summary>
    /// <param name="modules">Candidate modules.</param>
    /// <param name="layerProjects">Exact project paths for each pack layer that the solution supplies.</param>
    /// <returns>The candidate policy and the pack layers the solution does not supply.</returns>
    /// <exception cref="ArgumentException">The candidate is invalid, for example because a module has no projects.</exception>
    internal static (ArchitecturePolicy Policy, PolicyPackSuggestion Pack) Build(IEnumerable<ModulePolicy> modules,
        IReadOnlyDictionary<string, string[]> layerProjects)
    {
        var present = Layers.Where(layer => layerProjects.ContainsKey(layer.Id)).ToArray();
        var ids = present.Select(layer => layer.Id).ToHashSet(StringComparer.Ordinal);
        var layers = present.Select(layer => new LayerPolicy(layer.Id, layerProjects[layer.Id], layer.AllowedDependencies.Where(ids.Contains),
            domainInfrastructure: layer.ForbiddenNamespaces.Count > 0 ? new DomainInfrastructurePolicy(forbiddenNamespaces: layer.ForbiddenNamespaces) : null));
        var rules = new Dictionary<string, DiagnosticSeverity>(Rules, StringComparer.Ordinal)
        {
            // Module rules come from the extending document: the pack itself declares no logical modules.
            ["AG001"] = DiagnosticSeverity.Error,
            ["AG003"] = DiagnosticSeverity.Error,
            ["AG004"] = DiagnosticSeverity.Error,
        };
        var exclusions = Layers.Where(layer => !ids.Contains(layer.Id)).Select(layer => "layer:" + layer.Id).ToList();
        var repositoryLayers = RepositoryLayers.Where(ids.Contains).ToArray();
        if (repositoryLayers.Length == 0)
        {
            // Without a layer that may use repositories, ABP003 has nothing to allow; the pack's selection is excluded instead.
            rules.Remove("ABP003");
            exclusions.Add("rule:ABP003");
        }

        var policy = new ArchitecturePolicy(modules, layers, rules, abp: new AbpPolicy(repositoryLayers));
        return (policy, new PolicyPackSuggestion(PackId, PackVersion, exclusions));
    }

    private static IReadOnlyList<string> Layer(params string[] ids) => Array.AsReadOnly(ids);

    private static bool IsTest(string segment) => segment == "TestBase" || segment.EndsWith("Tests", StringComparison.Ordinal)
        || segment.EndsWith("TestApp", StringComparison.Ordinal);
}
