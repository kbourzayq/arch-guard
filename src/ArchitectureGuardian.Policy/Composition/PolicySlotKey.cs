namespace ArchitectureGuardian.Policy.Composition;

/// <summary>Identifies one independently mergeable policy setting, such as a layer's projects or a rule severity.</summary>
/// <remarks>
/// Composition merges at this granularity: a higher-precedence document replaces a whole setting, while
/// unrelated settings of the same declaration remain inherited. Ordering is ordinal and culture-independent.
/// </remarks>
internal sealed class PolicySlotKey : IEquatable<PolicySlotKey>, IComparable<PolicySlotKey>
{
    /// <summary>The field name representing the existence of a module, contract or layer declaration.</summary>
    internal const string Declaration = "";

    private readonly string _canonical;

    private PolicySlotKey(SlotKind kind, string id, string? contract, string field)
    {
        Kind = kind;
        Id = id;
        Contract = contract;
        Field = field;
        // IDs may contain dots, so a separator outside the identity grammar keeps keys unambiguous.
        _canonical = string.Join("\u001f", (int)kind, id, contract ?? string.Empty, field);
    }

    /// <summary>Distinguishes the kinds of declarations a setting belongs to.</summary>
    internal enum SlotKind
    {
        /// <summary>A logical module declaration or one of its fields.</summary>
        Module,

        /// <summary>A public contract declaration within a module, or its projects.</summary>
        Contract,

        /// <summary>An architectural layer declaration or one of its fields.</summary>
        Layer,

        /// <summary>A selected rule and its reporting severity.</summary>
        Rule,

        /// <summary>The quality-gate severity threshold.</summary>
        Gate,
    }

    /// <summary>Gets the declaration kind.</summary>
    internal SlotKind Kind { get; }

    /// <summary>Gets the module, layer or rule identity; empty for the gate.</summary>
    internal string Id { get; }

    /// <summary>Gets the contract identity for contract settings.</summary>
    internal string? Contract { get; }

    /// <summary>Gets the field name, or <see cref="Declaration"/> for the declaration itself.</summary>
    internal string Field { get; }

    /// <summary>Gets a user-facing logical path such as <c>layers.Domain.projects</c>.</summary>
    internal string Display => Kind switch
    {
        SlotKind.Module => Suffix("modules." + Id),
        SlotKind.Contract => Suffix("modules." + Id + ".contracts." + Contract),
        SlotKind.Layer => Suffix("layers." + Id),
        SlotKind.Rule => "rules." + Id,
        _ => "gate.failOn",
    };

    /// <summary>Creates a module declaration or field key.</summary>
    /// <param name="id">The module identity.</param>
    /// <param name="field">The field name, or the declaration marker.</param>
    /// <returns>The key.</returns>
    internal static PolicySlotKey Module(string id, string field = Declaration) => new(SlotKind.Module, id, null, field);

    /// <summary>Creates a contract declaration or field key.</summary>
    /// <param name="module">The owning module identity.</param>
    /// <param name="contract">The contract identity.</param>
    /// <param name="field">The field name, or the declaration marker.</param>
    /// <returns>The key.</returns>
    internal static PolicySlotKey ContractOf(string module, string contract, string field = Declaration) =>
        new(SlotKind.Contract, module, contract, field);

    /// <summary>Creates a layer declaration or field key.</summary>
    /// <param name="id">The layer identity.</param>
    /// <param name="field">The field name, or the declaration marker.</param>
    /// <returns>The key.</returns>
    internal static PolicySlotKey Layer(string id, string field = Declaration) => new(SlotKind.Layer, id, null, field);

    /// <summary>Creates a rule selection key.</summary>
    /// <param name="id">The canonical rule ID.</param>
    /// <returns>The key.</returns>
    internal static PolicySlotKey Rule(string id) => new(SlotKind.Rule, id, null, Declaration);

    /// <summary>Creates the quality-gate key.</summary>
    /// <returns>The key.</returns>
    internal static PolicySlotKey Gate() => new(SlotKind.Gate, string.Empty, null, Declaration);

    /// <inheritdoc />
    public bool Equals(PolicySlotKey? other) => other is not null && string.Equals(_canonical, other._canonical, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as PolicySlotKey);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_canonical);

    /// <inheritdoc />
    public int CompareTo(PolicySlotKey? other) => other is null ? 1 : string.CompareOrdinal(_canonical, other._canonical);

    private string Suffix(string prefix) => Field.Length == 0 ? prefix : prefix + "." + Field;
}
