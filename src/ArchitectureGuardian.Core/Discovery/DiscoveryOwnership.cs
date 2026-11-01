namespace ArchitectureGuardian.Core.Discovery;

/// <summary>Candidate project ownership inferred from explicit role-bearing names or namespaces.</summary>
/// <param name="ProjectPath">The observed project identity.</param>
/// <param name="Module">The candidate logical module.</param>
/// <param name="Layer">The candidate architectural role.</param>
/// <param name="Contract">A candidate whole-project public contract, or null.</param>
public sealed record DiscoveryOwnership(string ProjectPath, string Module, string Layer, string? Contract);
