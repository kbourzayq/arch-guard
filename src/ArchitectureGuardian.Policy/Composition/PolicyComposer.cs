using ArchitectureGuardian.Policy.Documents;
using ArchitectureGuardian.Policy.Packs;

namespace ArchitectureGuardian.Policy.Composition;

/// <summary>Resolves pinned rule packs and merges them into one validated policy with deterministic precedence.</summary>
/// <remarks>
/// <para>
/// Precedence: a document's own settings replace inherited settings field by field. Packs listed together in
/// one <c>extends</c> array are siblings with equal precedence; when siblings supply different values for the
/// same setting, composition fails unless the extending document overrides that setting. List order therefore
/// never changes the result.
/// </para>
/// <para>
/// Packs are declarative YAML resolved from embedded resources or host-supplied local manifests. Composition
/// never loads assemblies or executes extension code.
/// </para>
/// </remarks>
internal sealed class PolicyComposer
{
    private const int MaximumPacks = 32;
    private const int MaximumDepth = 16;

    private readonly IPolicyPackResolver? _resolver;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<string, ResolvedPack> _resolved = new(StringComparer.Ordinal);
    private readonly HashSet<string> _started = new(StringComparer.Ordinal);
    private readonly List<PolicyPackReference> _stack = [];

    /// <summary>Creates a composer for one policy load.</summary>
    /// <param name="resolver">The host's local-pack resolver, or null when only built-in packs are available.</param>
    /// <param name="cancellationToken">Cancellation checked before each pack is read.</param>
    internal PolicyComposer(IPolicyPackResolver? resolver, CancellationToken cancellationToken)
    {
        _resolver = resolver;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Composes the root policy and its packs into an immutable, validated document.</summary>
    /// <param name="root">The root policy fragment.</param>
    /// <returns>The validated policy.</returns>
    /// <exception cref="PolicyReadException">A pack is unavailable, incompatible or conflicting, or the composed policy is invalid.</exception>
    /// <exception cref="IOException">A host could not read a local pack manifest.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    internal PolicyDocument Compose(PolicyFragment root) => Build(root, Effective(root));

    private Dictionary<PolicySlotKey, PolicySlot> Effective(PolicyFragment fragment)
    {
        var inherited = new Dictionary<PolicySlotKey, PolicySlot>();
        var contributors = new Dictionary<PolicySlotKey, PolicyPackReference>();
        var conflicts = new Dictionary<PolicySlotKey, (PolicyPackReference First, PolicyPackReference Second)>();
        foreach (var reference in fragment.Extends)
        {
            foreach (var pair in Exclude(Load(reference), reference))
            {
                if (!inherited.TryGetValue(pair.Key, out var existing))
                {
                    inherited.Add(pair.Key, pair.Value);
                    contributors.Add(pair.Key, reference);
                }
                else if (existing.Comparison != pair.Value.Comparison && !conflicts.ContainsKey(pair.Key))
                {
                    conflicts.Add(pair.Key, (contributors[pair.Key], reference));
                }
            }
        }

        // The extending document wins over every pack it extends and resolves sibling conflicts it overrides.
        foreach (var pair in fragment.Slots)
        {
            inherited[pair.Key] = pair.Value;
            conflicts.Remove(pair.Key);
        }

        if (conflicts.Count > 0)
        {
            var conflict = conflicts.OrderBy(pair => pair.Key).First();
            throw (fragment.ExtendsOrigin ?? fragment.Root).Error(
                $"Packs '{conflict.Value.First.Label}' and '{conflict.Value.Second.Label}' set '{conflict.Key.Display}' to different values. Override '{conflict.Key.Display}' in {fragment.Root.Document} or exclude the declaration from one pack.");
        }

        return inherited;
    }

    private Dictionary<PolicySlotKey, PolicySlot> Load(PolicyPackReference reference)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var cycle = _stack.FindIndex(item => item.Id == reference.Id);
        if (cycle >= 0)
        {
            throw reference.Origin.Error("Pack extends cycle: "
                + string.Join(" → ", _stack.Skip(cycle).Select(item => item.Label).Concat([reference.Label]))
                + ". Remove one of the extends entries.");
        }

        if (_resolved.TryGetValue(reference.Id, out var resolved))
        {
            if (resolved.Reference.Identity != reference.Identity)
            {
                throw reference.Origin.Error(
                    $"Pack '{reference.Id}' is referenced as {resolved.Reference.Identity} and {reference.Identity}. Pin one version and source for each pack.");
            }

            // A reused subtree still counts toward the depth at this position, so limits do not depend on list order.
            if (_stack.Count + resolved.Height > MaximumDepth)
            {
                throw LimitError(reference);
            }

            return resolved.Slots;
        }

        // Count a pack when its resolution starts; packs still being resolved count as much as finished ones.
        if (_stack.Count >= MaximumDepth || _started.Count >= MaximumPacks)
        {
            throw LimitError(reference);
        }

        _started.Add(reference.Id);
        var builtIn = reference.RelativePath is null;
        var source = builtIn ? ReadBuiltIn(reference) : ReadLocal(reference);
        var fragment = new PolicyFragmentReader(source.SourcePath, $"pack '{reference.Label}'", builtIn)
            .ReadPack(source.Content, _cancellationToken);
        if (fragment.PackId != reference.Id || fragment.PackVersion != reference.Version)
        {
            throw fragment.Root.Error(
                $"The manifest declares '{fragment.PackId}@{fragment.PackVersion}', but {reference.Origin.Document} pins '{reference.Label}'. Update the pinned version or the manifest.");
        }

        _stack.Add(reference);
        var slots = Effective(fragment);
        _stack.RemoveAt(_stack.Count - 1);
        // Every extended pack is resolved by now; the height is this pack plus its deepest extends chain.
        var height = 1 + fragment.Extends.Select(child => _resolved[child.Id].Height).DefaultIfEmpty(0).Max();
        _resolved.Add(reference.Id, new ResolvedPack(reference, slots, height));
        return slots;
    }

    private static PolicyReadException LimitError(PolicyPackReference reference) => reference.Origin.Error(
        $"Rule-pack composition supports at most {MaximumPacks} packs and {MaximumDepth} levels of extends.");

    private static PolicyPackSource ReadBuiltIn(PolicyPackReference reference)
    {
        var content = BuiltInPolicyPacks.Read(reference.Id, reference.Version);
        if (content is not null)
        {
            return new PolicyPackSource(BuiltInPolicyPacks.SourcePath(reference.Id, reference.Version), content);
        }

        var versions = BuiltInPolicyPacks.Versions(reference.Id);
        throw reference.Origin.Error(versions.Count == 0
            ? $"Unknown built-in pack '{reference.Id}'. Available packs: {string.Join(", ", BuiltInPolicyPacks.Available)}. Add path for a local pack."
            : $"Built-in pack '{reference.Id}' has no version {reference.Version}. Available versions: {string.Join(", ", versions)}.");
    }

    private PolicyPackSource ReadLocal(PolicyPackReference reference)
    {
        var request = new PolicyPackRequest(reference.Id, reference.Version, reference.RelativePath!);
        if (_resolver is null)
        {
            throw reference.Origin.Error(
                $"Local pack '{reference.Label}' requires a host that reads files beside the policy. Load the policy from its file path.");
        }

        PolicyPackSource? source;
        try
        {
            source = _resolver.Resolve(request, _cancellationToken);
        }
        catch (ArgumentException exception)
        {
            throw reference.Origin.Error($"Cannot use local pack '{reference.Label}': {exception.Message}", exception);
        }

        return source ?? throw reference.Origin.Error(
            $"Local pack '{reference.Label}' was not found at '{request.ManifestPath}' relative to the policy directory. Compiler builds must also include the manifest as an AdditionalFiles item.");
    }

    private static Dictionary<PolicySlotKey, PolicySlot> Exclude(Dictionary<PolicySlotKey, PolicySlot> slots,
        PolicyPackReference reference)
    {
        if (reference.Exclusions.Count == 0)
        {
            return slots;
        }

        var result = new Dictionary<PolicySlotKey, PolicySlot>(slots);
        foreach (var exclusion in reference.Exclusions)
        {
            var separator = exclusion.Key.IndexOf(':');
            var kind = exclusion.Key.Substring(0, separator);
            var id = exclusion.Key.Substring(separator + 1);
            var declaration = kind switch
            {
                "layer" => PolicySlotKey.Layer(id),
                "module" => PolicySlotKey.Module(id),
                _ => PolicySlotKey.Rule(id),
            };
            if (!result.ContainsKey(declaration))
            {
                throw exclusion.Value.Error(
                    $"Pack '{reference.Label}' does not supply '{exclusion.Key}'. Remove it from exclude.");
            }

            var removedKind = kind switch
            {
                "layer" => PolicySlotKey.SlotKind.Layer,
                "module" => PolicySlotKey.SlotKind.Module,
                _ => PolicySlotKey.SlotKind.Rule,
            };
            foreach (var key in result.Keys.Where(key => key.Id == id && (key.Kind == removedKind
                         || removedKind == PolicySlotKey.SlotKind.Module && key.Kind == PolicySlotKey.SlotKind.Contract)).ToArray())
            {
                result.Remove(key);
            }

            // Removing a layer also removes it from the pack's EF Core persistence layers, keeping references valid.
            var persistenceLayers = PolicySlotKey.EntityFrameworkCore("persistenceLayers");
            if (removedKind == PolicySlotKey.SlotKind.Layer && result.TryGetValue(persistenceLayers, out var layersSlot))
            {
                result[persistenceLayers] = layersSlot.Without(target => target == id);
            }

            // Removing a declaration also removes the pack's permissions that target it, keeping references valid.
            foreach (var key in result.Keys.Where(key => key.Field == "allowedDependencies").ToArray())
            {
                if (removedKind == PolicySlotKey.SlotKind.Layer && key.Kind == PolicySlotKey.SlotKind.Layer)
                {
                    result[key] = result[key].Without(target => target == id);
                }
                else if (removedKind == PolicySlotKey.SlotKind.Module && key.Kind == PolicySlotKey.SlotKind.Module)
                {
                    result[key] = result[key].Without(target => target == "module:" + id
                        || target.StartsWith("contract:" + id + "/", StringComparison.Ordinal));
                }
            }
        }

        return result;
    }

    private static PolicyDocument Build(PolicyFragment root, Dictionary<PolicySlotKey, PolicySlot> slots)
    {
        var ordered = slots.OrderBy(pair => pair.Key).ToArray();
        var modules = ordered.Where(pair => pair.Key.Kind == PolicySlotKey.SlotKind.Module && pair.Key.Field.Length == 0)
            .Select(pair => Module(pair.Key.Id, pair.Value, slots)).ToArray();
        var layers = ordered.Where(pair => pair.Key.Kind == PolicySlotKey.SlotKind.Layer && pair.Key.Field.Length == 0)
            .Select(pair => Layer(pair.Key.Id, pair.Value, slots)).ToArray();
        var rules = ordered.Where(pair => pair.Key.Kind == PolicySlotKey.SlotKind.Rule)
            .ToDictionary(pair => pair.Key.Id, pair => pair.Value.Severity!.Value, StringComparer.Ordinal);
        var failOn = slots.TryGetValue(PolicySlotKey.Gate(), out var gate) ? gate.Severity!.Value : PolicySeverity.Error;
        if (modules.Length == 0 || layers.Length == 0 || rules.Count == 0)
        {
            var missing = modules.Length == 0 ? "modules" : layers.Length == 0 ? "layers" : "rules";
            throw root.Root.Field("." + missing).Error(
                $"The composed policy declares no {missing}. Declare {missing} in this policy or extend a pack that supplies them.");
        }

        ValidatePermissions(modules, layers, slots);
        var entityFrameworkCore = EntityFrameworkCore(root, layers, rules.Keys, slots);
        try
        {
            return new PolicyDocument(modules, layers, rules, failOn, root.Root.SourcePath, root.Locations, entityFrameworkCore);
        }
        catch (ArgumentException exception)
        {
            throw root.Root.Error(exception.Message, exception);
        }
    }

    /// <summary>Reports unknown permission targets at the document and field that supplied the effective list.</summary>
    /// <remarks>The document constructor repeats these checks; this pass only adds the precise origin.</remarks>
    private static void ValidatePermissions(ModulePolicy[] modules, LayerPolicy[] layers, Dictionary<PolicySlotKey, PolicySlot> slots)
    {
        var targets = new HashSet<string>(modules.Select(module => "module:" + module.Id), StringComparer.Ordinal);
        targets.UnionWith(modules.SelectMany(module => module.Contracts.Select(contract => "contract:" + module.Id + "/" + contract.Id)));
        foreach (var module in modules)
        {
            var unknown = module.AllowedDependencies.FirstOrDefault(target => !targets.Contains(target));
            if (unknown is not null)
            {
                throw slots[PolicySlotKey.Module(module.Id, "allowedDependencies")].Origin.Error(
                    $"modules.{module.Id}.allowedDependencies: Unknown target '{unknown}'; declare the module or contract and use module:ID or contract:MODULE/ID.");
            }
        }

        var layerIds = new HashSet<string>(layers.Select(layer => layer.Id), StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            var unknown = layer.AllowedDependencies.FirstOrDefault(target => !layerIds.Contains(target));
            if (unknown is not null)
            {
                throw slots[PolicySlotKey.Layer(layer.Id, "allowedDependencies")].Origin.Error(
                    $"layers.{layer.Id}.allowedDependencies: Unknown layer '{unknown}'; declare the layer first.");
            }
        }
    }

    /// <summary>Builds the effective EF Core settings, reporting invalid references at the field that supplied them.</summary>
    private static EntityFrameworkCorePolicy? EntityFrameworkCore(PolicyFragment root, LayerPolicy[] layers, IEnumerable<string> rules,
        Dictionary<PolicySlotKey, PolicySlot> slots)
    {
        var layersKey = PolicySlotKey.EntityFrameworkCore("persistenceLayers");
        var apisKey = PolicySlotKey.EntityFrameworkCore("forbiddenApis");
        slots.TryGetValue(layersKey, out var layersSlot);
        slots.TryGetValue(apisKey, out var apisSlot);
        var policy = layersSlot is null && apisSlot is null ? null
            : new EntityFrameworkCorePolicy(layersSlot?.Strings ?? [], apisSlot?.Strings ?? []);
        try
        {
            EntityFrameworkCorePolicy.Validate(policy, layers.Select(layer => layer.Id), rules);
            return policy;
        }
        catch (ArgumentException exception)
        {
            var origin = exception.Message.StartsWith("entityFrameworkCore.persistenceLayers", StringComparison.Ordinal)
                ? layersSlot?.Origin : apisSlot?.Origin;
            throw (origin ?? root.Root).Error(exception.Message, exception);
        }
    }

    private static ModulePolicy Module(string id, PolicySlot declaration, Dictionary<PolicySlotKey, PolicySlot> slots)
    {
        var contracts = slots.Where(pair => pair.Key.Kind == PolicySlotKey.SlotKind.Contract && pair.Key.Id == id
                                            && pair.Key.Field.Length == 0)
            .OrderBy(pair => pair.Key)
            .Select(pair => Create(pair.Value, () => new ContractPolicy(pair.Key.Contract!,
                Required(slots, PolicySlotKey.ContractOf(id, pair.Key.Contract!, "projects"), pair.Value, $"contract '{id}/{pair.Key.Contract}'"))))
            .ToArray();
        return Create(declaration, () => new ModulePolicy(id,
            Required(slots, PolicySlotKey.Module(id, "projects"), declaration, $"module '{id}'"), contracts,
            Optional(slots, PolicySlotKey.Module(id, "allowedDependencies")),
            Optional(slots, PolicySlotKey.Module(id, "forbiddenAssemblies")),
            Optional(slots, PolicySlotKey.Module(id, "forbiddenNamespaces"))));
    }

    private static LayerPolicy Layer(string id, PolicySlot declaration, Dictionary<PolicySlotKey, PolicySlot> slots) =>
        Create(declaration, () => new LayerPolicy(id,
            Required(slots, PolicySlotKey.Layer(id, "projects"), declaration, $"layer '{id}'"),
            Optional(slots, PolicySlotKey.Layer(id, "allowedDependencies")),
            Optional(slots, PolicySlotKey.Layer(id, "forbiddenAssemblies")),
            Optional(slots, PolicySlotKey.Layer(id, "forbiddenNamespaces")),
            slots.TryGetValue(PolicySlotKey.Layer(id, "domainInfrastructure"), out var infrastructure) ? infrastructure.Infrastructure : null));

    private static string[] Required(Dictionary<PolicySlotKey, PolicySlot> slots, PolicySlotKey key, PolicySlot declaration, string owner) =>
        slots.TryGetValue(key, out var slot) ? slot.Strings!
            : throw declaration.Origin.Field(".projects").Error(
                $"Missing required field 'projects' for {owner}. Declare projects here or in an extended pack.");

    private static string[] Optional(Dictionary<PolicySlotKey, PolicySlot> slots, PolicySlotKey key) =>
        slots.TryGetValue(key, out var slot) ? slot.Strings! : [];

    private static T Create<T>(PolicySlot declaration, Func<T> create)
    {
        try
        {
            return create();
        }
        catch (ArgumentException exception)
        {
            throw declaration.Origin.Error(exception.Message, exception);
        }
    }

    /// <summary>Caches one resolved pack's effective settings for diamond-shaped extends graphs.</summary>
    private sealed class ResolvedPack
    {
        internal ResolvedPack(PolicyPackReference reference, Dictionary<PolicySlotKey, PolicySlot> slots, int height)
        {
            Reference = reference;
            Slots = slots;
            Height = height;
        }

        internal PolicyPackReference Reference { get; }

        internal Dictionary<PolicySlotKey, PolicySlot> Slots { get; }

        /// <summary>Gets the number of extends levels from this pack to its deepest descendant, including itself.</summary>
        internal int Height { get; }
    }
}
