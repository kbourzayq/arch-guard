using ArchitectureGuardian.Core.Canonicalization;

namespace ArchitectureGuardian.Core.Rules;

/// <summary>Retains rule-owned provenance for deterministic explanations, independently of rendered messages.</summary>
public sealed class ViolationContext
{
    /// <summary>Snapshots a directed dependency path, violated constraints and declared contract alternatives.</summary>
    /// <param name="dependencyPath">Observed endpoints in traversal order; a cycle repeats its starting module.</param>
    /// <param name="policyConstraints">Constraints established by the evaluator, with relevant configured values.</param>
    /// <param name="publicContracts">Declared contract:MODULE/ID alternatives, without promising API suitability or permission.</param>
    public ViolationContext(IEnumerable<ViolationPathNode> dependencyPath, IEnumerable<PolicyConstraint> policyConstraints,
        IEnumerable<string>? publicContracts = null)
    {
        ArgumentNullException.ThrowIfNull(dependencyPath);
        var path = dependencyPath.ToArray();
        if (path.Length < 2 || path.Any((ViolationPathNode? node) => node is null || string.IsNullOrWhiteSpace(node.Kind) || string.IsNullOrWhiteSpace(node.Identity)))
        {
            throw new ArgumentException("An observed path requires at least two nonempty endpoints.", nameof(dependencyPath));
        }

        DependencyPath = Array.AsReadOnly(path);
        PolicyConstraints = CanonicalData.Snapshot(policyConstraints,
            item => CanonicalData.Encode(item.Scope, item.Setting, CanonicalData.Encode(item.Values.ToArray())));
        PublicContracts = CanonicalData.Snapshot(publicContracts ?? [], value => value);
    }

    /// <summary>Gets observed endpoints in path order, without inferring a transitive closure.</summary>
    public IReadOnlyList<ViolationPathNode> DependencyPath { get; }
    /// <summary>Gets the actual violated settings or built-in invariants.</summary>
    public IReadOnlyList<PolicyConstraint> PolicyConstraints { get; }
    /// <summary>Gets configured contract alternatives; module and layer permissions still apply independently.</summary>
    public IReadOnlyList<string> PublicContracts { get; }
}
