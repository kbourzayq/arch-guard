using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Mcp;

/// <summary>Runs the onboarding workflow through the trusted MCP stdio server.</summary>
[Trait("Category", "Integration")]
[Collection(nameof(McpOnboardingWorkflowTests))]
public sealed class McpOnboardingWorkflowTests : OnboardingWorkflowTests
{
    /// <inheritdoc />
    protected override bool UseMcp => true;
}
