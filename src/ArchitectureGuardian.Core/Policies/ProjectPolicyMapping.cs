namespace ArchitectureGuardian.Core.Policies;

/// <summary>Identifies a project's single module, layer and optional public contract.</summary>
/// <param name="ProjectPath">The normalized repository-relative project identity.</param>
/// <param name="ModuleId">The owning module.</param>
/// <param name="LayerId">The architectural layer.</param>
/// <param name="ContractId">The local public contract identity, or null for implementation projects.</param>
public sealed record ProjectPolicyMapping(string ProjectPath, string ModuleId, string LayerId, string? ContractId);
