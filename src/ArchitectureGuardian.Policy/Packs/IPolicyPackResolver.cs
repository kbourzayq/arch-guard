namespace ArchitectureGuardian.Policy.Packs;

/// <summary>Supplies local rule-pack manifests to policy composition without exposing host file systems to the reader.</summary>
/// <remarks>
/// Built-in packs are resolved by the reader itself. Hosts implement this port to read local packs from the
/// repository (CLI/MCP) or from compiler-supplied additional files. Packs are declarative data only; resolving
/// a pack never loads or executes extension code.
/// </remarks>
public interface IPolicyPackResolver
{
    /// <summary>Reads the manifest of a local pack.</summary>
    /// <param name="request">The pinned pack and its directory relative to the root policy.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>The manifest content, or null when no manifest exists at the requested location.</returns>
    /// <exception cref="ArgumentException">The location is unsafe or the manifest exceeds host limits.</exception>
    /// <exception cref="IOException">The manifest exists but cannot be read.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    PolicyPackSource? Resolve(PolicyPackRequest request, CancellationToken cancellationToken);
}
