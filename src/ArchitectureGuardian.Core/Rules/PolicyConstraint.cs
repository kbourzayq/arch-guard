using ArchitectureGuardian.Core.Canonicalization;

namespace ArchitectureGuardian.Core.Rules;

/// <summary>Identifies a violated policy scope and its relevant configured values, without claiming YAML source coordinates.</summary>
public sealed class PolicyConstraint
{
    /// <summary>Snapshots a policy constraint or a built-in rule invariant.</summary>
    /// <param name="scope">Symbolic module/layer scope, or rule:AG003 for the built-in cycle invariant.</param>
    /// <param name="setting">The setting whose constraint was violated.</param>
    /// <param name="values">Configured values relevant to the finding; empty for a missing permission or built-in invariant.</param>
    public PolicyConstraint(string scope, string setting, IEnumerable<string> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(setting);
        Scope = scope;
        Setting = setting;
        Values = CanonicalData.Snapshot(values, value => value);
    }

    /// <summary>Gets the symbolic owning scope, rather than a parsed YAML path.</summary>
    public string Scope { get; }
    /// <summary>Gets the configured setting or built-in invariant name.</summary>
    public string Setting { get; }
    /// <summary>Gets the relevant values in ordinal order.</summary>
    public IReadOnlyList<string> Values { get; }
}
