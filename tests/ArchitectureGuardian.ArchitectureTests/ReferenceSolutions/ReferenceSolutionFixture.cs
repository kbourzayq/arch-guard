using ArchitectureGuardian.ArchitectureTests.Infrastructure;

namespace ArchitectureGuardian.ArchitectureTests.ReferenceSolutions;

/// <summary>Restores isolated copies of the checked-in M1 reference solutions.</summary>
/// <remarks>The Incomplete scenario deliberately references an absent assembly and contains unresolved types.</remarks>
public sealed class ReferenceSolutionFixture() : CopiedSolutionFixture("ReferenceSolutions", "archguard-reference-");
