namespace ArchitectureGuardian.Tests.Workspaces;

/// <summary>Restores only the ASP.NET Core fixture, which uses the SDK's targeting pack and needs no packages.</summary>
public sealed class AspNetCoreWorkspaceFixture() : WorkspaceFixture("AspNetCore/AspNetCore.slnx", includesGenerators: false);
