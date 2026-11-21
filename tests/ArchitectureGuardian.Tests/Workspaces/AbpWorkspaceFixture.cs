namespace ArchitectureGuardian.Tests.Workspaces;

/// <summary>Restores only the ABP Framework fixture, whose projects reference the Volo.Abp packages.</summary>
public sealed class AbpWorkspaceFixture() : WorkspaceFixture("Abp/BookStore.slnx", includesGenerators: false);
