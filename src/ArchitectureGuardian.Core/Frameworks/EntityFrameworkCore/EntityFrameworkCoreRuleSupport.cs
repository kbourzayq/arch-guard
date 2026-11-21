namespace ArchitectureGuardian.Core.Frameworks.EntityFrameworkCore;

/// <summary>Publishes what an EF Core rule can establish statically and what it cannot.</summary>
/// <param name="RuleId">The EF rule ID this support statement describes.</param>
/// <param name="SupportedCases">Static source shapes the rule evaluates.</param>
/// <param name="RuntimeBlindSpots">Runtime behavior the rule never infers; applications must validate these separately.</param>
public sealed record EntityFrameworkCoreRuleSupport(string RuleId, IReadOnlyList<string> SupportedCases, IReadOnlyList<string> RuntimeBlindSpots);
