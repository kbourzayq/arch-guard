using ArchitectureGuardian.Policy.Composition;
using ArchitectureGuardian.Policy.Documents;
using ArchitectureGuardian.Policy.Packs;

namespace ArchitectureGuardian.Policy;

/// <summary>Parses strict YAML v1 data without file access or compiler/workspace dependencies.</summary>
/// <remarks>
/// Policies may extend pinned rule packs. Built-in packs are embedded in this assembly; local packs require a
/// host-supplied <see cref="IPolicyPackResolver"/>. Packs are declarative and never execute code.
/// </remarks>
public sealed class PolicyDocumentReader
{
    /// <summary>Validates a single YAML document with schema-equivalent structural checks and semantic reference checks.</summary>
    /// <param name="yaml">The document content.</param>
    /// <param name="sourcePath">The document name used in errors.</param>
    /// <param name="cancellationToken">Cancellation checked before and during parsing and before returning.</param>
    /// <returns>The immutable, validated v1 policy, composed with any built-in packs it extends.</returns>
    /// <exception cref="PolicyReadException">The document does not conform to the supported v1 policy, or references a local pack.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public PolicyDocument Parse(string yaml, string sourcePath = "architecture.guardian.yaml",
        CancellationToken cancellationToken = default) => Parse(yaml, sourcePath, null, cancellationToken);

    /// <summary>Validates a policy and composes the built-in and local rule packs it extends.</summary>
    /// <param name="yaml">The document content.</param>
    /// <param name="sourcePath">The document name used in errors.</param>
    /// <param name="packResolver">Reads local pack manifests relative to the policy directory; null allows built-in packs only.</param>
    /// <param name="cancellationToken">Cancellation checked during parsing and before each pack is read.</param>
    /// <returns>The immutable, validated v1 policy with deterministic pack precedence applied.</returns>
    /// <exception cref="PolicyReadException">
    /// The policy or a pack manifest is invalid, a pack is unknown, incompatible, cyclic or conflicting, or the composed policy is invalid.
    /// </exception>
    /// <exception cref="IOException">The resolver could not read a local pack manifest.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public PolicyDocument Parse(string yaml, string sourcePath, IPolicyPackResolver? packResolver,
        CancellationToken cancellationToken = default)
    {
        if (yaml is null) { throw new ArgumentNullException(nameof(yaml)); }
        if (string.IsNullOrWhiteSpace(sourcePath)) { throw new ArgumentException("Source path cannot be blank."); }
        cancellationToken.ThrowIfCancellationRequested();
        var root = new PolicyFragmentReader(sourcePath, "the policy").ReadPolicy(yaml, cancellationToken);
        var document = new PolicyComposer(packResolver, cancellationToken).Compose(root);
        cancellationToken.ThrowIfCancellationRequested();
        return document;
    }
}
