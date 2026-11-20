using ArchitectureGuardian.ArchitectureTests.Infrastructure;

namespace ArchitectureGuardian.ArchitectureTests.RulePacks;

/// <summary>Restores isolated copies of the solutions that adopt the built-in rule packs.</summary>
public sealed class RulePackFixture() : CopiedSolutionFixture("RulePacks", "archguard-packs-");
