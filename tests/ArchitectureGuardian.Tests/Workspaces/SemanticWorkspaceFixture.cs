namespace ArchitectureGuardian.Tests.Workspaces;

/// <summary>Restores only the semantic matrix, partial references and generated-source inputs.</summary>
public sealed class SemanticWorkspaceFixture() : WorkspaceFixture("SemanticPreparation.slnx", includesGenerators: true);
