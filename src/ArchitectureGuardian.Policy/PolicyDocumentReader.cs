using System.Globalization;
using ArchitectureGuardian.Policy.Documents;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace ArchitectureGuardian.Policy;

/// <summary>Parses strict YAML v1 data without file access or compiler/workspace dependencies.</summary>
public sealed class PolicyDocumentReader
{
    /// <summary>Validates a single YAML document with schema-equivalent structural checks and semantic reference checks.</summary>
    /// <param name="yaml">The document content.</param>
    /// <param name="sourcePath">The document name used in errors.</param>
    /// <param name="cancellationToken">Cancellation checked before and during parsing and before returning.</param>
    /// <returns>The immutable, validated v1 policy.</returns>
    /// <exception cref="PolicyReadException">The document does not conform to the supported v1 policy.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public PolicyDocument Parse(string yaml, string sourcePath = "architecture.guardian.yaml",
        CancellationToken cancellationToken = default)
    {
        if (yaml is null) { throw new ArgumentNullException(nameof(yaml)); }
        if (string.IsNullOrWhiteSpace(sourcePath)) { throw new ArgumentException("Source path cannot be blank."); }
        cancellationToken.ThrowIfCancellationRequested();
        var reader = new PolicyReader(sourcePath);
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
                    throw reader.Error(current.Start, "$",
                        "Policy exceeds the supported 32 nesting levels or 100000 YAML events.");
                }

                if (current is AnchorAlias || current is NodeEvent node && (!node.Anchor.IsEmpty || !node.Tag.IsEmpty)
                                           || current is DocumentStart { Version: not null })
                {
                    throw reader.Error(current.Start, "$",
                        "YAML aliases, anchors, explicit tags and version directives are unsupported. Use a single plain data document.");
                }
            }

            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count != 1)
            {
                throw reader.Error(Mark.Empty, "$", "Expected exactly one YAML policy document.");
            }

            var result = reader.Read(stream.Documents[0].RootNode);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (YamlException exception)
        {
            throw reader.Error(exception.Start, "$", "Invalid YAML: " + exception.Message, exception);
        }
    }

    /// <summary>Enforces the published v1 object shape and retains document positions at the adapter boundary.</summary>
    private sealed class PolicyReader(string sourcePath)
    {
        private readonly Dictionary<string, PolicyPosition> _locations = new(StringComparer.Ordinal);
        internal PolicyDocument Read(YamlNode root)
        {
            var fields = Map(root, "$", ["version", "modules", "layers", "rules"], ["gate"]);
            if (fields["version"] is not YamlScalarNode { Value: "1", Style: ScalarStyle.Plain })
            {
                throw Error(fields["version"].Start, "$.version",
                    "Unsupported schema version. Use the integer version: 1.");
            }

            var modules = Sequence(fields["modules"], "$.modules")
                .Select((node, index) => Module(node, $"$.modules[{index}]")).ToArray();
            var layers = Sequence(fields["layers"], "$.layers")
                .Select((node, index) => Layer(node, $"$.layers[{index}]")).ToArray();
            var rulesNode = fields["rules"];
            var rules = Map(rulesNode, "$.rules", [], new[] { "AG001", "AG002", "AG003", "AG004", "AG005", "AG006" });
            if (rules.Count == 0)
            {
                throw Error(rulesNode.Start, "$.rules", "Select at least one canonical rule (AG001–AG006).");
            }

            var severities = rules.ToDictionary(pair => pair.Key, pair => Severity(pair.Value, "$.rules." + pair.Key),
                StringComparer.Ordinal);
            var gate = PolicySeverity.Error;
            if (fields.TryGetValue("gate", out var gateNode))
            {
                var gateFields = Map(gateNode, "$.gate", ["failOn"], []);
                gate = Severity(gateFields["failOn"], "$.gate.failOn");
            }

            return Construct(root, "$", () => new PolicyDocument(modules, layers, severities, gate, sourcePath, _locations));
        }

        private ModulePolicy Module(YamlNode node, string path)
        {
            var fields = Map(node, path, ["id", "projects"],
                ["contracts", "allowedDependencies", "forbiddenAssemblies", "forbiddenNamespaces"]);
            var contracts = fields.TryGetValue("contracts", out var value)
                ? Sequence(value, path + ".contracts", allowEmpty: true).Select((contract, index) =>
                {
                    var contractPath = $"{path}.contracts[{index}]";
                    var contractFields = Map(contract, contractPath, ["id", "projects"], []);
                    return Construct(contract, contractPath, () => new ContractPolicy(
                        Text(contractFields["id"], contractPath + ".id"),
                        Strings(contractFields["projects"], contractPath + ".projects")));
                }).ToArray()
                : [];
            return Construct(node, path, () => new ModulePolicy(Text(fields["id"], path + ".id"),
                Strings(fields["projects"], path + ".projects"), contracts,
                OptionalStrings(fields, "allowedDependencies", path),
                OptionalStrings(fields, "forbiddenAssemblies", path),
                OptionalStrings(fields, "forbiddenNamespaces", path)));
        }

        private LayerPolicy Layer(YamlNode node, string path)
        {
            var fields = Map(node, path, ["id", "projects"],
                ["allowedDependencies", "forbiddenAssemblies", "forbiddenNamespaces", "domainInfrastructure"]);
            DomainInfrastructurePolicy? infrastructure = null;
            if (fields.TryGetValue("domainInfrastructure", out var value))
            {
                var scope = path + ".domainInfrastructure";
                var restrictions = Map(value, scope, [],
                    ["forbiddenAssemblies", "forbiddenNamespaces", "allowedNamespaces"]);
                infrastructure = Construct(value, scope, () => new DomainInfrastructurePolicy(
                    OptionalStrings(restrictions, "forbiddenAssemblies", scope),
                    OptionalStrings(restrictions, "forbiddenNamespaces", scope),
                    OptionalStrings(restrictions, "allowedNamespaces", scope)));
            }

            return Construct(node, path, () => new LayerPolicy(Text(fields["id"], path + ".id"),
                Strings(fields["projects"], path + ".projects"), OptionalStrings(fields, "allowedDependencies", path),
                OptionalStrings(fields, "forbiddenAssemblies", path),
                OptionalStrings(fields, "forbiddenNamespaces", path), infrastructure));
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
                var keyNode = pair.Key;
                var value = pair.Value;
                var key = Text(keyNode, path);
                if (!required.Contains(key, StringComparer.Ordinal) && !optional.Contains(key, StringComparer.Ordinal))
                {
                    throw Error(keyNode.Start, path + "." + key,
                        $"Unknown or unsupported field '{key}'. Supported fields: {string.Join(", ", required.Concat(optional))}.");
                }

                if (fields.ContainsKey(key))
                {
                    throw Error(keyNode.Start, path + "." + key, $"Duplicate field '{key}'.");
                }
                fields.Add(key, value);
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
            Sequence(node, path, allowEmpty)
                .Select((child, index) => Text(child, $"{path}[{index}]")).ToArray();

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
                throw Error(node.Start, path,
                    "Expected a nonblank string. Quote strings that look like null, booleans or numbers.");
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

        private T Construct<T>(YamlNode node, string path, Func<T> create)
        {
            _locations[path] = Position(node.Start);
            try
            {
                return create();
            }
            catch (ArgumentException exception)
            {
                throw Error(node.Start, path, exception.Message, exception);
            }
        }

        internal PolicyReadException
            Error(Mark mark, string path, string message, Exception? exception = null) =>
            new(message, path, sourcePath, Position(mark), exception);

        private static PolicyPosition Position(Mark mark) =>
            new((int)Math.Max(1, Math.Min(mark.Line, int.MaxValue)), (int)Math.Max(1, Math.Min(mark.Column, int.MaxValue)));
    }
}
