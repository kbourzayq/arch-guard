using ArchitectureGuardian.Core.Canonicalization;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Baselines;

/// <summary>Identifies accepted architecture debt independently of wording, severity and source coordinates.</summary>
public sealed class BaselineEntry
{
    /// <summary>Snapshots the rule, target and semantic context used by the version-one fingerprint.</summary>
    /// <param name="ruleId">Case-sensitive rule identity.</param>
    /// <param name="target">Logical module and bound symbol, when known.</param>
    /// <param name="dependencyPath">Ordered semantic endpoints; project paths remain significant.</param>
    /// <param name="constraints">Violated policy settings and relevant values.</param>
    /// <param name="snapshotId">Exact diagnostic identity when structured context is unavailable.</param>
    /// <exception cref="ArgumentException">Identity is blank or context is unavailable without a snapshot identity.</exception>
    public BaselineEntry(string ruleId, DiagnosticTarget target, IEnumerable<ViolationPathNode> dependencyPath,
        IEnumerable<PolicyConstraint> constraints, string? snapshotId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dependencyPath);
        var path = dependencyPath.ToArray();
        if (path.Any(node => node is null || string.IsNullOrWhiteSpace(node.Kind) || string.IsNullOrWhiteSpace(node.Identity))
            || (path.Length < 2 && snapshotId is null) || snapshotId is not null && string.IsNullOrWhiteSpace(snapshotId))
        {
            throw new ArgumentException("A semantic path or a nonblank snapshot identity is required.", nameof(dependencyPath));
        }

        RuleId = ruleId;
        Target = target;
        DependencyPath = Array.AsReadOnly(path);
        Constraints = CanonicalData.Snapshot(constraints, ConstraintKey);
        SnapshotId = snapshotId;
        Fingerprint = CanonicalData.Hash(CanonicalData.Encode("ag-baseline-v1", ruleId, target.Module, target.Symbol,
            CanonicalData.Encode(path.Select(node => CanonicalData.Encode(node.Kind, node.Identity)).ToArray()),
            CanonicalData.Encode(Constraints.Select(ConstraintKey).ToArray()), snapshotId));
    }

    /// <summary>Gets the lowercase SHA-256 of the versioned semantic identity.</summary>
    public string Fingerprint { get; }
    /// <summary>Gets the case-sensitive rule identifier.</summary>
    public string RuleId { get; }
    /// <summary>Gets the affected logical module and symbol.</summary>
    public DiagnosticTarget Target { get; }
    /// <summary>Gets semantic endpoints in traversal order.</summary>
    public IReadOnlyList<ViolationPathNode> DependencyPath { get; }
    /// <summary>Gets violated settings in canonical order.</summary>
    public IReadOnlyList<PolicyConstraint> Constraints { get; }
    /// <summary>Gets the conservative exact-match fallback for findings lacking semantic context.</summary>
    public string? SnapshotId { get; }

    /// <summary>Creates a fingerprint from evaluator-owned context, falling back to exact snapshot identity.</summary>
    /// <param name="diagnostic">The current finding, whose reporting severity does not affect matching.</param>
    public static BaselineEntry FromDiagnostic(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        var context = diagnostic.Violation.Context;
        return new(diagnostic.RuleId, diagnostic.Target, context?.DependencyPath ?? [], context?.PolicyConstraints ?? [],
            context is null ? diagnostic.Id : null);
    }

    private static string ConstraintKey(PolicyConstraint constraint) =>
        CanonicalData.Encode(constraint.Scope, constraint.Setting, CanonicalData.Encode(constraint.Values.ToArray()));
}
