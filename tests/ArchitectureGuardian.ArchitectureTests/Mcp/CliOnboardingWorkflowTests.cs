using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Mcp;

/// <summary>Runs the onboarding workflow through the CLI process.</summary>
[Trait("Category", "Integration")]
[Collection(nameof(CliOnboardingWorkflowTests))]
public sealed class CliOnboardingWorkflowTests : OnboardingWorkflowTests
{
    /// <inheritdoc />
    protected override bool UseMcp => false;
}
