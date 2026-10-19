using System.Xml;
using System.Xml.Linq;
using ArchitectureGuardian.Core.Rules;
using Microsoft.CodeAnalysis;

namespace ArchitectureGuardian.Analyzers.Policies;

/// <summary>Validates a supplied assembly ownership snapshot without discovering or loading projects.</summary>
internal sealed class CompilerPolicy
{
    internal const string FileName = "architecture.guardian.compiler.xml";
    private readonly Dictionary<string, string[]> _modules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _layers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Ownership> _assemblies = new(StringComparer.Ordinal);

    private readonly Dictionary<string, (string[] Assemblies, string[] Namespaces)> _restrictions = new(StringComparer.Ordinal);

    private readonly Dictionary<string, (string[] Assemblies, string[] Namespaces, string[] AllowedNamespaces)> _infrastructure = new(StringComparer.Ordinal);

    private CompilerPolicy() { }

    internal static CompilerPolicy Load(AdditionalText file, CancellationToken cancellationToken)
    {
        var text = file.GetText(cancellationToken) ?? throw new ArgumentException("Cannot read compiler policy AdditionalFile.");
        cancellationToken.ThrowIfCancellationRequested();
        using var input = new System.IO.StringReader(text.ToString());
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var root = XDocument.Load(reader).Root;
        if (root is null || root.Name != "ArchitectureGuardian" || (string?)root.Attribute("version") != "1")
        {
            throw new ArgumentException("Expected ArchitectureGuardian root with version='1'.");
        }

        Validate(root, "version");
        var policy = new CompilerPolicy();
        foreach (var element in root.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (element.Name.ToString())
            {
                case "Module":
                    Validate(element, "id", "allows", "forbiddenAssemblies", "forbiddenNamespaces");
                    policy.ReadRestrictions(element);
                    policy._modules.Add(Required(element, "id"), Permissions(element));
                    break;
                case "Layer":
                    Validate(element, "id", "allows", "forbiddenAssemblies", "forbiddenNamespaces",
                        "infrastructureAssemblies", "infrastructureNamespaces", "allowedInfrastructureNamespaces");
                    policy.ReadRestrictions(element);
                    var assemblies = Permissions(element, "infrastructureAssemblies");
                    var namespaces = Permissions(element, "infrastructureNamespaces");
                    var allowed = Permissions(element, "allowedInfrastructureNamespaces");
                    if (assemblies.Any(name => !ForbiddenReferenceSemantics.IsAssemblyName(name))
                        || namespaces.Concat(allowed).Any(name => !ForbiddenReferenceSemantics.IsNamespaceName(name)))
                    {
                        throw new ArgumentException("Invalid AG006 infrastructure assembly or namespace name; use exact simple names and dot-separated namespace identifiers without wildcards.");
                    }

                    policy._infrastructure.Add(Required(element, "id"), (assemblies, namespaces, allowed));
                    policy._layers.Add(Required(element, "id"), Permissions(element));
                    break;
                case "Assembly":
                    Validate(element, "name", "module", "layer", "contract");
                    policy._assemblies.Add(Required(element, "name"), new Ownership(Required(element, "module"),
                        Required(element, "layer"), element.Attribute("contract") is null ? null : Required(element, "contract")));
                    break;
                default:
                    throw new ArgumentException("Unknown compiler policy element: " + element.Name);
            }
        }

        foreach (var ownership in policy._assemblies.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!policy._modules.ContainsKey(ownership.Module) || !policy._layers.ContainsKey(ownership.Layer))
            {
                throw new ArgumentException("Assembly refers to an undeclared module or layer.");
            }
        }

        var targets = new HashSet<string>(policy._modules.Keys.Select(id => "module:" + id), StringComparer.Ordinal);
        targets.UnionWith(policy._assemblies.Values.Where(owner => owner.Contract is not null)
            .Select(owner => "contract:" + owner.Module + "/" + owner.Contract));
        if (policy._modules.Values.SelectMany(value => value).Any(value => !targets.Contains(value))
            || policy._layers.Values.SelectMany(value => value).Any(value => !policy._layers.ContainsKey(value)))
        {
            throw new ArgumentException("Permission refers to an undeclared module, contract or layer.");
        }

        return policy;
    }

    internal bool Contains(string assemblyName) => _assemblies.ContainsKey(assemblyName);

    internal IEnumerable<(string RuleId, string Source, string Target)> Violations(string sourceAssembly, string targetAssembly)
    {
        if (!_assemblies.TryGetValue(sourceAssembly, out var source) || !_assemblies.TryGetValue(targetAssembly, out var target))
        {
            yield break;
        }

        if (!DependencySemantics.AllowsModule(source.Module, target.Module, target.Contract, _modules[source.Module]))
        {
            yield return (DependencySemantics.ModuleId, source.Module, target.Module);
        }

        if (!DependencySemantics.AllowsImplementation(source.Module, target.Module, target.Contract))
        {
            yield return (DependencySemantics.ImplementationId, source.Module, target.Module);
        }

        if (!DependencySemantics.AllowsLayer(source.Layer, target.Layer, _layers[source.Layer]))
        {
            yield return (DependencySemantics.LayerId, source.Layer, target.Layer);
        }
    }

    internal IEnumerable<(string Scope, string Kind, string Name)> Restrictions(string sourceAssembly, string targetAssembly, string targetNamespace)
    {
        var owner = _assemblies[sourceAssembly];
        foreach (var scope in new[] { "modules." + owner.Module, "layers." + owner.Layer })
        {
            var restrictions = _restrictions[scope];
            foreach (var name in restrictions.Assemblies)
            {
                if (targetAssembly == name) { yield return (scope, "forbiddenAssemblies", name); }
            }

            foreach (var name in restrictions.Namespaces)
            {
                if (ForbiddenReferenceSemantics.MatchesNamespace(targetNamespace, name)) { yield return (scope, "forbiddenNamespaces", name); }
            }
        }
    }

    internal IEnumerable<(string Scope, string Kind, string Name)> InfrastructureRestrictions(string sourceAssembly, string targetAssembly, string targetNamespace)
    {
        var layer = _assemblies[sourceAssembly].Layer;
        var restrictions = _infrastructure[layer];
        foreach (var (kind, name) in DomainInfrastructureSemantics.Restrictions(targetAssembly, targetNamespace,
            restrictions.Assemblies, restrictions.Namespaces, restrictions.AllowedNamespaces))
        {
            yield return ("layers." + layer + ".domainInfrastructure", kind, name);
        }
    }

    private void ReadRestrictions(XElement element)
    {
        var assemblies = Permissions(element, "forbiddenAssemblies");
        var namespaces = Permissions(element, "forbiddenNamespaces");
        if (assemblies.Any(name => !ForbiddenReferenceSemantics.IsAssemblyName(name))
            || namespaces.Any(name => !ForbiddenReferenceSemantics.IsNamespaceName(name)))
        {
            throw new ArgumentException("Invalid forbiddenAssemblies or forbiddenNamespaces; use exact simple names and dot-separated namespace identifiers without wildcards.");
        }

        _restrictions.Add((element.Name == "Module" ? "modules." : "layers.") + Required(element, "id"), (assemblies, namespaces));
    }

    private static string Required(XElement element, string attribute)
    {
        var value = (string?)element.Attribute(attribute);
        if (string.IsNullOrWhiteSpace(value) || value!.Any(character => !char.IsLetterOrDigit(character) && character is not ('.' or '_' or '-')))
        {
            throw new ArgumentException(element.Name + " requires a nonempty identifier in '" + attribute + "' (letters, digits, '.', '_' or '-').");
        }

        return value!;
    }

    private static string[] Permissions(XElement element, string attribute = "allows")
    {
        var values = ((string?)element.Attribute(attribute) ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw new ArgumentException("Duplicate permissions in " + element.Name + ".");
        }

        return values;
    }

    private static void Validate(XElement element, params string[] attributes)
    {
        if (element.Attributes().Any(attribute => !attributes.Contains(attribute.Name.ToString(), StringComparer.Ordinal))
            || element.Name != "ArchitectureGuardian" && element.HasElements
            || element.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
        {
            throw new ArgumentException("Unsupported attributes or content in " + element.Name + ".");
        }
    }

    /// <summary>Assembly-level neutral ownership for a single compiler snapshot.</summary>
    private sealed class Ownership(string module, string layer, string? contract)
    {
        internal string Module { get; } = module;
        internal string Layer { get; } = layer;
        internal string? Contract { get; } = contract;
    }
}
