namespace ArchitectureGuardian.Tests.Workspaces;

/// <summary>Restores only the EF Core fixture, whose projects reference the EF Core packages.</summary>
public sealed class EfCoreWorkspaceFixture() : WorkspaceFixture("EfCore/EfCore.slnx", includesGenerators: false);
