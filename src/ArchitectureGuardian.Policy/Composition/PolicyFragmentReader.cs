using System.Globalization;
using System.Text.RegularExpressions;
using ArchitectureGuardian.Policy.Documents;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
using Version = System.Version;

namespace ArchitectureGuardian.Policy.Composition;

/// <summary>Reads one strict YAML policy or rule-pack manifest into validated, uncomposed settings.</summary>
/// <remarks>
/// A policy without <c>extends</c> keeps the complete v1 shape: every required field is checked here. Policies
/// that extend packs, and pack manifests, may declare partial entries; composition checks completeness after
/// inherited settings are merged. Each value is validated in its own document so errors point at their source.
/// </remarks>
internal sealed class PolicyFragmentReader
{
    /// <summary>The canonical rule IDs this engine evaluates.</summary>
    internal static readonly string[] RuleIds = ["AG001", "AG002", "AG003", "AG004", "AG005", "AG006", "ASP001", "ASP002", "ASP003", "ASP004", "ASP005", "EF001", "EF002", "EF003"];

    private static readonly Regex PackIdPattern = new("\\A[a-z][a-z0-9]*(-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new("\\A(0|[1-9][0-9]{0,8})\\.(0|[1-9][0-9]{0,8})\\.(0|[1-9][0-9]{0,8})\\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex ExclusionPattern = new("\\A(layer|module|rule):[A-Za-z][A-Za-z0-9_.-]*\\z", RegexOptions.CultureInvariant);

    private readonly string _sourcePath;
    private readonly string _document;
    private readonly bool _builtIn;
    private PolicyFragment _fragment = null!;

    /// <summary>Creates a reader for one document.</summary>
    /// <param name="sourcePath">The path shown in errors.</param>
    /// <param name="document">A human-readable label, such as "the policy" or "pack 'id@1.0.0'".</param>
    /// <param name="builtIn">Whether the document is an embedded pack, which cannot reference local packs.</param>
    internal PolicyFragmentReader(string sourcePath, string document, bool builtIn = false)
    {
        _sourcePath = sourcePath;
        _document = document;
        _builtIn = builtIn;
    }

    /// <summary>Gets the engine version that pack <c>requires.engine</c> constraints are compared against.</summary>
    internal static Version EngineVersion { get; } = CreateEngineVersion();

    /// <summary>Reads a root policy document.</summary>
    /// <param name="yaml">The document content.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>The document's own settings and pack references.</returns>
    /// <exception cref="PolicyReadException">The document is not a valid v1 policy.</exception>
    internal PolicyFragment ReadPolicy(string yaml, CancellationToken cancellationToken)
    {
        var root = Load(yaml, "policy", cancellationToken);
        _fragment = new PolicyFragment(Origin(root, "$"));
        _fragment.Locations["$"] = Position(root.Start);
        var fields = Map(root, "$", ["version"], ["extends", "modules", "layers", "rules", "gate", "entityFrameworkCore"]);
        if (fields["version"] is not YamlScalarNode { Value: "1", Style: ScalarStyle.Plain })
        {
            throw Error(fields["version"].Start, "$.version", "Unsupported schema version. Use the integer version: 1.");
        }

        var extended = fields.TryGetValue("extends", out var extends);
        if (!extended)
        {
            foreach (var key in new[] { "modules", "layers", "rules" })
            {
                if (!fields.ContainsKey(key))
                {
                    throw Error(root.Start, "$." + key, $"Missing required field '{key}'.");
                }
            }
        }
        else
        {
            ReadExtends(extends!);
        }

        ReadDeclarations(fields, strict: !extended);
        ReadRules(fields, required: !extended, pack: false);
        ReadGate(fields);
        ReadEntityFrameworkCore(fields);
        cancellationToken.ThrowIfCancellationRequested();
        return _fragment;
    }

    /// <summary>Reads a rule-pack manifest and checks its compatibility with this engine.</summary>
    /// <param name="yaml">The manifest content.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>The pack's own settings, identity and pack references.</returns>
    /// <exception cref="PolicyReadException">The manifest is invalid or incompatible.</exception>
    internal PolicyFragment ReadPack(string yaml, CancellationToken cancellationToken)
    {
        var root = Load(yaml, "pack manifest", cancellationToken);
        _fragment = new PolicyFragment(Origin(root, "$"));
        var fields = Map(root, "$", ["manifest", "id", "version", "requires", "rules"],
            ["description", "extends", "modules", "layers", "gate", "entityFrameworkCore"]);
        if (fields["manifest"] is not YamlScalarNode { Value: "1", Style: ScalarStyle.Plain })
        {
            throw Error(fields["manifest"].Start, "$.manifest", "Unsupported pack manifest version. Use the integer manifest: 1.");
        }

        _fragment.PackId = PackId(fields["id"], "$.id");
        _fragment.PackVersion = SemanticVersion(fields["version"], "$.version");
        if (fields.TryGetValue("description", out var description))
        {
            _ = Text(description, "$.description");
        }

        ReadRequirements(fields["requires"]);
        if (fields.TryGetValue("extends", out var extends))
        {
            ReadExtends(extends);
        }

        ReadDeclarations(fields, strict: false);
        ReadRules(fields, required: true, pack: true);
        ReadGate(fields);
        ReadEntityFrameworkCore(fields);
        cancellationToken.ThrowIfCancellationRequested();
        return _fragment;
    }

    private YamlNode Load(string yaml, string kind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Reject graph-like YAML features before constructing nodes: policies are finite, unambiguous trees.
            var parser = new Parser(new StringReader(yaml));
            var depth = 0;
            var events = 0;
            while (parser.MoveNext())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = parser.Current!;
                depth += current.NestingIncrease;
                if (++events > 100_000 || depth > 32)
                {
                    throw Error(current.Start, "$", $"The {kind} exceeds the supported 32 nesting levels or 100000 YAML events.");
                }

                if (current is AnchorAlias || current is NodeEvent node && (!node.Anchor.IsEmpty || !node.Tag.IsEmpty)
                                           || current is DocumentStart { Version: not null })
                {
                    throw Error(current.Start, "$",
                        "YAML aliases, anchors, explicit tags and version directives are unsupported. Use a single plain data document.");
                }
            }

            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count != 1)
            {
                throw Error(Mark.Empty, "$", $"Expected exactly one YAML {kind} document.");
            }

            return stream.Documents[0].RootNode;
        }
        catch (YamlException exception)
        {
            throw Error(exception.Start, "$", "Invalid YAML: " + exception.Message, exception);
        }
    }

    private void ReadRequirements(YamlNode node)
    {
        var label = _fragment.PackId + "@" + _fragment.PackVersion;
        var fields = Map(node, "$.requires", ["policySchema", "engine"], []);
        var schema = fields["policySchema"];
        if (schema is not YamlScalarNode { Value: "1", Style: ScalarStyle.Plain })
        {
            throw Error(schema.Start, "$.requires.policySchema",
                $"Pack '{label}' targets policy schema '{(schema as YamlScalarNode)?.Value}', but this engine supports policy schema 1. Pin a pack version compatible with this release.");
        }

        var engine = SemanticVersion(fields["engine"], "$.requires.engine");
        if (ParseVersion(engine) > EngineVersion)
        {
            throw Error(fields["engine"].Start, "$.requires.engine",
                $"Pack '{label}' requires Architecture Guardian {engine} or later, but this engine is {EngineVersion.ToString(3)}. Upgrade Architecture Guardian or pin an older pack version.");
        }
    }

    private void ReadExtends(YamlNode node)
    {
        _fragment.ExtendsOrigin = Origin(node, "$.extends");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = Sequence(node, "$.extends");
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var path = $"$.extends[{index}]";
            var fields = Map(entry, path, ["pack", "version"], ["path", "exclude"]);
            var id = PackId(fields["pack"], path + ".pack");
            if (!seen.Add(id))
            {
                throw Error(fields["pack"].Start, path + ".pack", $"Pack '{id}' is listed more than once in extends.");
            }

            var version = SemanticVersion(fields["version"], path + ".version");
            string? local = null;
            if (fields.TryGetValue("path", out var pathNode))
            {
                if (_builtIn)
                {
                    throw Error(pathNode.Start, path + ".path", "Built-in packs can extend only other built-in packs. Remove path.");
                }

                local = LocalPath(pathNode, path + ".path");
            }

            var exclusions = new List<KeyValuePair<string, PolicyOrigin>>();
            if (fields.TryGetValue("exclude", out var excludeNode))
            {
                var targets = Sequence(excludeNode, path + ".exclude");
                for (var target = 0; target < targets.Count; target++)
                {
                    var targetPath = $"{path}.exclude[{target}]";
                    var value = Text(targets[target], targetPath);
                    if (!ExclusionPattern.IsMatch(value)
                        || value.StartsWith("rule:", StringComparison.Ordinal) && !RuleIds.Contains(value.Substring(5), StringComparer.Ordinal))
                    {
                        throw Error(targets[target].Start, targetPath,
                            $"Invalid exclusion '{value}'. Use layer:ID, module:ID or rule:ID for a declaration or rule supplied by the pack.");
                    }

                    if (exclusions.Any(item => item.Key == value))
                    {
                        throw Error(targets[target].Start, targetPath, $"Duplicate exclusion '{value}'.");
                    }

                    exclusions.Add(new(value, Origin(targets[target], targetPath)));
                }
            }

            _fragment.Extends.Add(new PolicyPackReference(id, version, local, exclusions, Origin(entry, path)));
        }
    }

    private void ReadDeclarations(Dictionary<string, YamlNode> fields, bool strict)
    {
        if (fields.TryGetValue("modules", out var modules))
        {
            var entries = Sequence(modules, "$.modules", allowEmpty: !strict);
            for (var index = 0; index < entries.Count; index++)
            {
                Module(entries[index], $"$.modules[{index}]", strict);
            }
        }

        if (fields.TryGetValue("layers", out var layers))
        {
            var entries = Sequence(layers, "$.layers", allowEmpty: !strict);
            for (var index = 0; index < entries.Count; index++)
            {
                Layer(entries[index], $"$.layers[{index}]", strict);
            }
        }
    }

    private void Module(YamlNode node, string path, bool strict)
    {
        var fields = Map(node, path, strict ? ["id", "projects"] : ["id"],
            strict ? ["contracts", "allowedDependencies", "forbiddenAssemblies", "forbiddenNamespaces"]
                : ["projects", "contracts", "allowedDependencies", "forbiddenAssemblies", "forbiddenNamespaces"]);
        var id = Identity(fields["id"], path + ".id");
        Declare(PolicySlotKey.Module(id), fields["id"], node, path, "module");
        AddValues(fields, "projects", path, PolicySlotKey.Module(id, "projects"), values => PolicyData.Patterns(values));
        AddValues(fields, "allowedDependencies", path, PolicySlotKey.Module(id, "allowedDependencies"),
            values => PolicyData.Strings(values), allowEmpty: true);
        AddValues(fields, "forbiddenAssemblies", path, PolicySlotKey.Module(id, "forbiddenAssemblies"),
            values => PolicyData.ReferenceNames(values, assembly: true), allowEmpty: true);
        AddValues(fields, "forbiddenNamespaces", path, PolicySlotKey.Module(id, "forbiddenNamespaces"),
            values => PolicyData.ReferenceNames(values, assembly: false), allowEmpty: true);
        if (!fields.TryGetValue("contracts", out var contracts))
        {
            return;
        }

        var entries = Sequence(contracts, path + ".contracts", allowEmpty: true);
        for (var index = 0; index < entries.Count; index++)
        {
            var contractPath = $"{path}.contracts[{index}]";
            var contractFields = Map(entries[index], contractPath, strict ? ["id", "projects"] : ["id"], strict ? [] : ["projects"]);
            var contract = Identity(contractFields["id"], contractPath + ".id");
            Declare(PolicySlotKey.ContractOf(id, contract), contractFields["id"], entries[index], contractPath, "contract");
            AddValues(contractFields, "projects", contractPath, PolicySlotKey.ContractOf(id, contract, "projects"),
                values => PolicyData.Patterns(values));
        }
    }

    private void Layer(YamlNode node, string path, bool strict)
    {
        var fields = Map(node, path, strict ? ["id", "projects"] : ["id"],
            strict ? ["allowedDependencies", "forbiddenAssemblies", "forbiddenNamespaces", "domainInfrastructure"]
                : ["projects", "allowedDependencies", "forbiddenAssemblies", "forbiddenNamespaces", "domainInfrastructure"]);
        var id = Identity(fields["id"], path + ".id");
        Declare(PolicySlotKey.Layer(id), fields["id"], node, path, "layer");
        AddValues(fields, "projects", path, PolicySlotKey.Layer(id, "projects"), values => PolicyData.Patterns(values));
        AddValues(fields, "allowedDependencies", path, PolicySlotKey.Layer(id, "allowedDependencies"),
            PolicyData.Strings, allowEmpty: true);
        AddValues(fields, "forbiddenAssemblies", path, PolicySlotKey.Layer(id, "forbiddenAssemblies"),
            values => PolicyData.ReferenceNames(values, assembly: true), allowEmpty: true);
        AddValues(fields, "forbiddenNamespaces", path, PolicySlotKey.Layer(id, "forbiddenNamespaces"),
            values => PolicyData.ReferenceNames(values, assembly: false), allowEmpty: true);
        if (!fields.TryGetValue("domainInfrastructure", out var value))
        {
            return;
        }

        var scope = path + ".domainInfrastructure";
        var restrictions = Map(value, scope, [], ["forbiddenAssemblies", "forbiddenNamespaces", "allowedNamespaces"]);
        _fragment.Locations[scope] = Position(value.Start);
        var infrastructure = Validate(value, scope, () => new DomainInfrastructurePolicy(
            OptionalStrings(restrictions, "forbiddenAssemblies", scope),
            OptionalStrings(restrictions, "forbiddenNamespaces", scope),
            OptionalStrings(restrictions, "allowedNamespaces", scope)));
        _fragment.Slots[PolicySlotKey.Layer(id, "domainInfrastructure")] =
            PolicySlot.ForInfrastructure(infrastructure, Origin(value, scope));
    }

    private void ReadRules(Dictionary<string, YamlNode> fields, bool required, bool pack)
    {
        if (!fields.TryGetValue("rules", out var node))
        {
            return;
        }

        if (pack && node is YamlMappingNode mapping)
        {
            foreach (var key in mapping.Children.Keys)
            {
                var id = Text(key, "$.rules");
                if (!RuleIds.Contains(id, StringComparer.Ordinal))
                {
                    throw Error(key.Start, "$.rules." + id,
                        $"Pack '{_fragment.PackId}@{_fragment.PackVersion}' requires rule '{id}', which this engine does not provide (AG001–AG006, ASP001–ASP005, EF001–EF003). Upgrade Architecture Guardian or pin a compatible pack version.");
                }
            }
        }

        var rules = Map(node, "$.rules", [], RuleIds);
        if (rules.Count == 0 && required)
        {
            throw Error(node.Start, "$.rules", pack
                ? "Declare at least one rule ID this pack configures (AG001–AG006, ASP001–ASP005, EF001–EF003)."
                : "Select at least one canonical rule (AG001–AG006, ASP001–ASP005, EF001–EF003).");
        }

        foreach (var pair in rules)
        {
            var path = "$.rules." + pair.Key;
            _fragment.Slots[PolicySlotKey.Rule(pair.Key)] = PolicySlot.ForSeverity(Severity(pair.Value, path), Origin(pair.Value, path));
        }
    }

    private void ReadGate(Dictionary<string, YamlNode> fields)
    {
        if (!fields.TryGetValue("gate", out var gateNode))
        {
            return;
        }

        var gateFields = Map(gateNode, "$.gate", ["failOn"], []);
        var failOn = gateFields["failOn"];
        _fragment.Slots[PolicySlotKey.Gate()] = PolicySlot.ForSeverity(Severity(failOn, "$.gate.failOn"), Origin(failOn, "$.gate.failOn"));
    }

    private void ReadEntityFrameworkCore(Dictionary<string, YamlNode> fields)
    {
        if (!fields.TryGetValue("entityFrameworkCore", out var node))
        {
            return;
        }

        const string scope = "$.entityFrameworkCore";
        var settings = Map(node, scope, [], ["persistenceLayers", "forbiddenApis"]);
        _fragment.Locations[scope] = Position(node.Start);
        // Each list merges independently, so a policy can replace a pack's forbidden APIs and keep its persistence layers.
        AddValues(settings, "persistenceLayers", scope, PolicySlotKey.EntityFrameworkCore("persistenceLayers"),
            values => new EntityFrameworkCorePolicy(persistenceLayers: values), allowEmpty: true);
        AddValues(settings, "forbiddenApis", scope, PolicySlotKey.EntityFrameworkCore("forbiddenApis"),
            values => new EntityFrameworkCorePolicy(forbiddenApis: values), allowEmpty: true);
    }

    private void Declare(PolicySlotKey key, YamlNode idNode, YamlNode node, string path, string kind)
    {
        if (_fragment.Slots.ContainsKey(key))
        {
            throw Error(idNode.Start, path + ".id", $"Duplicate {kind} '{key.Contract ?? key.Id}'.");
        }

        _fragment.Locations[path] = Position(node.Start);
        _fragment.Slots[key] = PolicySlot.ForDeclaration(Origin(node, path));
    }

    private void AddValues(Dictionary<string, YamlNode> fields, string field, string path, PolicySlotKey key,
        Func<string[], object> validate, bool allowEmpty = false)
    {
        if (!fields.TryGetValue(field, out var node))
        {
            return;
        }

        var fieldPath = path + "." + field;
        var values = Strings(node, fieldPath, allowEmpty);
        _ = Validate(node, fieldPath, () => validate(values));
        _fragment.Slots[key] = PolicySlot.ForStrings(values, Origin(node, fieldPath));
    }

    private string Identity(YamlNode node, string path)
    {
        var value = Text(node, path);
        return Validate(node, path, () => PolicyData.Id(value));
    }

    private string PackId(YamlNode node, string path)
    {
        var value = Text(node, path);
        if (value.Length > 64 || !PackIdPattern.IsMatch(value))
        {
            throw Error(node.Start, path,
                $"Invalid pack identity '{value}'. Use at most 64 lowercase letters, digits and single hyphens, starting with a letter.");
        }

        return value;
    }

    private string SemanticVersion(YamlNode node, string path)
    {
        var value = Text(node, path);
        if (!VersionPattern.IsMatch(value))
        {
            throw Error(node.Start, path,
                $"Invalid version '{value}'. Pin an exact MAJOR.MINOR.PATCH version such as 1.0.0; ranges, wildcards and prerelease labels are unsupported.");
        }

        return value;
    }

    private string LocalPath(YamlNode node, string path)
    {
        var value = Text(node, path);
        var segments = value.Split('/');
        if (value.IndexOf('\\') >= 0 || value.IndexOf(':') >= 0 || value.Length > 1024
            || segments.Any(segment => segment.Length == 0 || segment == "." || segment == ".." || segment.Any(char.IsControl)))
        {
            throw Error(node.Start, path,
                "Use a pack directory relative to the root policy directory with '/' separators, such as packs/acme. Absolute paths, drive prefixes, '.', '..' and empty segments are unsupported.");
        }

        return value;
    }

    private Dictionary<string, YamlNode> Map(YamlNode node, string path, string[] required, string[] optional)
    {
        if (node is not YamlMappingNode mapping)
        {
            throw Error(node.Start, path, "Expected an object mapping.");
        }

        var fields = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
        foreach (var pair in mapping.Children)
        {
            var key = Text(pair.Key, path);
            if (!required.Contains(key, StringComparer.Ordinal) && !optional.Contains(key, StringComparer.Ordinal))
            {
                throw Error(pair.Key.Start, path + "." + key,
                    $"Unknown or unsupported field '{key}'. Supported fields: {string.Join(", ", required.Concat(optional))}.");
            }

            if (fields.ContainsKey(key))
            {
                throw Error(pair.Key.Start, path + "." + key, $"Duplicate field '{key}'.");
            }

            fields.Add(key, pair.Value);
        }

        foreach (var key in required)
        {
            if (!fields.ContainsKey(key))
            {
                throw Error(node.Start, path + "." + key, $"Missing required field '{key}'.");
            }
        }

        return fields;
    }

    private IList<YamlNode> Sequence(YamlNode node, string path, bool allowEmpty = false)
    {
        if (node is not YamlSequenceNode sequence || !allowEmpty && sequence.Children.Count == 0)
        {
            throw Error(node.Start, path, allowEmpty ? "Expected an array." : "Expected a nonempty array.");
        }

        return sequence.Children;
    }

    private string[] Strings(YamlNode node, string path, bool allowEmpty = false) =>
        Sequence(node, path, allowEmpty).Select((child, index) => Text(child, $"{path}[{index}]")).ToArray();

    private string[] OptionalStrings(Dictionary<string, YamlNode> fields, string key, string path) =>
        fields.TryGetValue(key, out var value) ? Strings(value, path + "." + key, allowEmpty: true) : [];

    private string Text(YamlNode node, string path)
    {
        if (node is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value)
                                              || scalar.Style == ScalarStyle.Plain && (scalar.Value is "~"
                                                  || scalar.Value!.Equals("null", StringComparison.OrdinalIgnoreCase)
                                                  || bool.TryParse(scalar.Value, out _)
                                                  || double.TryParse(scalar.Value, NumberStyles.Float,
                                                      CultureInfo.InvariantCulture, out _)))
        {
            throw Error(node.Start, path, "Expected a nonblank string. Quote strings that look like null, booleans or numbers.");
        }

        return scalar.Value!;
    }

    private PolicySeverity Severity(YamlNode node, string path) => Text(node, path) switch
    {
        "info" => PolicySeverity.Info,
        "warning" => PolicySeverity.Warning,
        "error" => PolicySeverity.Error,
        _ => throw Error(node.Start, path,
            "Supported severities are info, warning and error (case-sensitive); off/none is unsupported. Omit a rule ID to deselect it."),
    };

    private T Validate<T>(YamlNode node, string path, Func<T> create)
    {
        try
        {
            return create();
        }
        catch (ArgumentException exception)
        {
            throw Error(node.Start, path, exception.Message, exception);
        }
    }

    private PolicyOrigin Origin(YamlNode node, string path) => new(_sourcePath, path, Position(node.Start), _document);

    private PolicyReadException Error(Mark mark, string path, string message, Exception? exception = null) =>
        new(message, path, _sourcePath, Position(mark), exception);

    private static PolicyPosition Position(Mark mark) =>
        new((int)Math.Max(1, Math.Min(mark.Line, int.MaxValue)), (int)Math.Max(1, Math.Min(mark.Column, int.MaxValue)));

    /// <summary>Parses a validated MAJOR.MINOR.PATCH version.</summary>
    /// <param name="value">A version matching the reader's semantic-version pattern.</param>
    /// <returns>The comparable version.</returns>
    internal static Version ParseVersion(string value)
    {
        var parts = value.Split('.');
        return new Version(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    private static Version CreateEngineVersion()
    {
        var version = typeof(PolicyFragmentReader).Assembly.GetName().Version ?? new Version(0, 0, 0);
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }
}
