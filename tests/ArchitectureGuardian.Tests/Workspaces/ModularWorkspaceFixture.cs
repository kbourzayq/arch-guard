namespace ArchitectureGuardian.Tests.Workspaces;

/// <summary>Restores only module fixtures for reference-restriction parity tests.</summary>
public sealed class ModularWorkspaceFixture() : WorkspaceFixture("ModularMonolith/ModularMonolith.slnx", includesGenerators: false);
