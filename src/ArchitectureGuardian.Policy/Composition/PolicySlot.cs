using ArchitectureGuardian.Policy.Documents;

namespace ArchitectureGuardian.Policy.Composition;

/// <summary>Holds one validated policy setting together with the document that supplied it.</summary>
internal sealed class PolicySlot
{
    private PolicySlot(string[]? strings, PolicySeverity? severity, DomainInfrastructurePolicy? infrastructure,
        string comparison, PolicyOrigin origin)
    {
        Strings = strings;
        Severity = severity;
        Infrastructure = infrastructure;
        Comparison = comparison;
        Origin = origin;
    }

    /// <summary>Gets a string-list value, such as selectors or allowed dependencies.</summary>
    internal string[]? Strings { get; }

    /// <summary>Gets a severity value for a rule or gate.</summary>
    internal PolicySeverity? Severity { get; }

    /// <summary>Gets an AG006 domain-infrastructure restriction value.</summary>
    internal DomainInfrastructurePolicy? Infrastructure { get; }

    /// <summary>Gets canonical text used to detect conflicting values independently of list order.</summary>
    internal string Comparison { get; }

    /// <summary>Gets the document and field that supplied the value.</summary>
    internal PolicyOrigin Origin { get; }

    /// <summary>Creates a marker recording that a module, contract or layer is declared.</summary>
    /// <param name="origin">The declaring entry.</param>
    /// <returns>The slot.</returns>
    internal static PolicySlot ForDeclaration(PolicyOrigin origin) => new(null, null, null, string.Empty, origin);

    /// <summary>Creates a string-list value.</summary>
    /// <param name="values">The validated values.</param>
    /// <param name="origin">The supplying field.</param>
    /// <returns>The slot.</returns>
    internal static PolicySlot ForStrings(IEnumerable<string> values, PolicyOrigin origin)
    {
        var copy = values.ToArray();
        return new(copy, null, null, Canonical(copy), origin);
    }

    /// <summary>Creates a severity value.</summary>
    /// <param name="severity">The severity.</param>
    /// <param name="origin">The supplying field.</param>
    /// <returns>The slot.</returns>
    internal static PolicySlot ForSeverity(PolicySeverity severity, PolicyOrigin origin) =>
        new(null, severity, null, severity.ToString(), origin);

    /// <summary>Creates a domain-infrastructure restriction value, which merges as one unit.</summary>
    /// <param name="infrastructure">The validated restrictions.</param>
    /// <param name="origin">The supplying field.</param>
    /// <returns>The slot.</returns>
    internal static PolicySlot ForInfrastructure(DomainInfrastructurePolicy infrastructure, PolicyOrigin origin) =>
        new(null, null, infrastructure, string.Join("\u001e", Canonical(infrastructure.ForbiddenAssemblies),
            Canonical(infrastructure.ForbiddenNamespaces), Canonical(infrastructure.AllowedNamespaces)), origin);

    /// <summary>Creates a copy of a string-list slot without the specified values.</summary>
    /// <param name="remove">Values to remove.</param>
    /// <returns>The adjusted slot, retaining its origin.</returns>
    internal PolicySlot Without(Func<string, bool> remove) => ForStrings(Strings!.Where(value => !remove(value)), Origin);

    private static string Canonical(IEnumerable<string> values) =>
        string.Join("\u001f", values.OrderBy(value => value, StringComparer.Ordinal));
}
